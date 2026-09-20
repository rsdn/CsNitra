#nullable enable

using Diagnostics;

namespace ExtensibleParser;

using ExtensibleParser.Recovery;

// Recovery-подсистема: состояние, хуки и цикл восстановления.
public partial class Parser
{
    private int _recoveryPoint = -1;
    private int _quietZoneEnd = -1;
    private FailureSnapshot? _lastSnapshot;
    private bool _suppressSideEffects;
    public FailureSnapshot? LastSnapshot => _lastSnapshot;
    private Result? _lastPartial;
    public Result? LastPartial => _lastPartial;
    private readonly List<RecoveryDiagnostic> _recoveryDiagnostics = [];
    public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics => _recoveryDiagnostics;

    // Хук для тестов (паттерн LastSnapshot/LastPartial): число ленивых вызовов RecoveryEngine.Generate в последнем Parse.
    public int EngineGenerateCalls { get; private set; }

    // Хук для тестов (2.4, бенчмарк): число проходов recovery-цикла (итераций) в последнем Parse.
    // Сбрасывается в начале Parse, инкрементируется на каждой итерации цикла восстановления.
    public int RecoveryPasses { get; private set; }

    // Хук для тестов (2.1/B1, точная гигиена): число удалений memo, выполненных HygieneCore за последний Parse.
    // Сбрасывается в начале Parse; ограниченно (не пропорционально размеру файла) при точной гигиене.
    public int HygieneRemovals { get; private set; }

    // Хук для тестов (5a.2.2, дешёвые S2-сканы): число позиций, реально проверенных S2-сканом за последний Recover.
    // Инкремент — в RecoveryEngine (5a.2.2); сброс — в начале Recover.
    public int S2ScanPositions { get; private set; }

    // Хук для тестов (5a.2.3, дешёвые S3-сканы): число позиций, реально проверенных S3-сканом за последний Recover.
    // Инкремент — в RecoveryEngine (5a.2.3); сброс — в начале Recover.
    public int S3ScanPositions { get; private set; }

    // Note-методы (5a.2.1a): публичные точки инкремента для движка (у счётчиков private set).
    public void NoteS2ScanPosition() => S2ScanPositions++;
    public void NoteS3ScanPosition() => S3ScanPositions++;

    // B2: кэш спекулятивных парсов (обёртка SpeculativeCache) + D2-счётчики хитов/промахов (read-only,
    // читаются из обёртки). Время жизни кэша — один Recover; сброс (кэш + счётчики) — в начале Recover.
    public SpeculativeCache SpecCache => _specCache;
    public int SpecCacheHits => _specCache.Hits;
    public int SpecCacheMisses => _specCache.Misses;

    // A3: единый источник recovery-лимитов. Convenience-свойства ниже читают/пишут Profile,
    // сохраняя существующие тесты, которые ставят их напрямую.
    public RecoveryProfile Profile { get; set; } = RecoveryProfile.Ide;

    // Лимиты цикла восстановления: предельное число итераций (Profile.MaxIterations).
    public int MaxRecoveryIterations
    {
        get => Profile.MaxIterations;
        set => Profile = Profile with { MaxIterations = value };
    }

    // A2: бюджеты по тирам (не по кандидатам). Каждый тир — свой под-бюджет: число кандидатов тира,
    // допустимое на точке восстановления. «Попыткой» считается кандидат тира; S0 (ранг 0) и S6 (ранг 6)
    // вне под-бюджетов (S6 — гарантированное дно, 1.3.2). Под-бюджеты раздельны, поэтому S1 (много
    // вставок) не выедает общий бюджет и не глушит S2/S3/S6. Значения — Profile (A3 RecoveryProfile).
    public int S1TierBudget
    {
        get => Profile.S1TierBudget;
        set => Profile = Profile with { S1TierBudget = value };
    }
    public int S2TierBudget
    {
        get => Profile.S2TierBudget;
        set => Profile = Profile with { S2TierBudget = value };
    }
    public int S3S6TierBudget
    {
        get => Profile.S3S6TierBudget;
        set => Profile = Profile with { S3S6TierBudget = value };
    }

    // Legacy (до A2): пер-позиционный бюджет кандидатов. Сохранён для совместимости (тесты его ставят),
    // но более не ограничивает выбор кандидатов — вместо него под-бюджеты тиров (A2).
    public int MaxRecoveryAttemptsPerPosition { get; set; } = 3;
    private readonly Dictionary<int, TierBudget> _attempts = new();

    // B2: кэш спекулятивных парсов на scratch-копии, время жизни = один Recover (сброс в начале Recover).
    private readonly SpeculativeCache _specCache = new();

    // Состояние, перенесённое из ядра: используется только recovery-подсистемой
    // (ядро обращается к нему исключительно через partial-хуки в Parser.cs).
    private FollowSetCalculator? _followCalculator;
    private readonly List<StackFrame> _stackFrames = [];
    private readonly Dictionary<(int Pos, Terminal Terminal), Injection> _injections = new(TerminalComparer.KeyComparer);
    private int _parseDepth;
    private int _maxParseDepth = 4096;

    // Хук для тестов/engine: инжекции в Фазе 0 никто не порождает, слой активен с Фазы 1.
    public void AddInjection(Terminal terminal, int pos, Injection injection) => _injections[(pos, terminal)] = injection;

    // Хуки engine (1.2): применение/откат инъекций с сохранением старого значения.
    public IReadOnlyDictionary<(int Pos, Terminal Terminal), Injection> Injections => _injections;
    public void ApplyInjection(Terminal terminal, int pos, Injection injection)
    {
        var key = (pos, terminal);
        OnInjectionApplied(key);
        _injections[key] = injection;
    }

    // oldValue == null — ключа не было (удалить), иначе — вернуть старое значение.
    public void RollbackInjection(Terminal terminal, int pos, Injection? oldValue)
    {
        if (oldValue is { } old)
            _injections[(pos, terminal)] = old;
        else
            _injections.Remove((pos, terminal));
    }

    // Хуки engine (1.2): доступ к memo для патчей/инспекции.
    public IReadOnlyDictionary<(int pos, string rule, int precedence), Result> Memo => _memo;
    public void SetMemo(string rule, int pos, int precedence, Result value)
    {
        var key = (pos, rule, precedence);
        OnMemoWritten(key);
        _memo[key] = value;
    }

    public void RemoveMemo(string rule, int pos, int precedence)
    {
        var key = (pos, rule, precedence);
        OnMemoRemoved(key);
        _memo.Remove(key);
    }

    // Патч для всех прецедентов (e, rule, prec'), присутствующих в memo (TDOPP, §3.4 S3).
    // Порядок Hygiene→Apply (A1/S6): Hygiene может удалить Failure на (pos, rule) до Apply —
    // тогда ключей нет и без этого патч no-op'ит, re-парс не видит абсорбер (регресс S2/S3 +
    // потеря memo, ломающего рекурсию NotPredicate → stack overflow). Создаём прецедент 0.
    public void PatchMemo(string rule, int pos, Result value)
    {
        var keys = _memo.Keys.Where(k => k.pos == pos && k.rule == rule).ToList();
        if (keys.Count == 0)
            keys.Add((pos, rule, 0));
        foreach (var key in keys)
        {
            OnMemoWritten(key);
            _memo[key] = value;
        }
    }

    public FollowSetCalculator? FollowCalculator => _followCalculator;
    public Terminal[] GetTerminators(IReadOnlyList<StackFrame> stack) =>
        _followCalculator?.GetTerminators(stack) ?? [EofTerminal.Instance];
    public IReadOnlyList<StackFrame> CurrentStackFrames => _stackFrames;

    // Одноразовый parse правила без recovery-цикла (спекулятивная валидация engine'а, §3.4 S2).
    public Result ParseRuleOnce(string ruleName, int minPrecedence, int startPos, string input) =>
        ParseRule(ruleName, minPrecedence, startPos, input);

    // Логи патчей текущего кандидата (memo и инъекции): заполняются хуками, пока активны, — основа отката.
    private List<MemoPatch>? _memoPatchLog;
    private List<InjectionPatch>? _injectionPatchLog;

    // ============ Реализации хуков (декларации — в Parser.cs) ============

    private partial void OnMismatch(Terminal terminal, int pos) => CaptureSnapshot(pos, terminal);
    private partial void OnMemoWritten((int pos, string rule, int precedence) key)
    {
        IndexMemoAdd(key);
        RecordMemo(key);
    }

    private partial void OnMemoRemoved((int pos, string rule, int precedence) key)
    {
        IndexMemoRemove(key);
        RecordMemoRemove(key);
    }
    private partial void OnInjectionApplied((int Pos, Terminal Terminal) key) => RecordInjection(key);
    private partial void OnPartialCaptured(Result partialResult) => _lastPartial = partialResult;
    private partial bool IsRecoveryPosition(int pos) => pos == _recoveryPoint;
    private partial bool InQuietZone(int pos) => _quietZoneEnd >= 0 && pos <= _quietZoneEnd;
    private partial bool SuppressSideEffects => _suppressSideEffects;
    private partial void ResetRecoveryPoint() => _recoveryPoint = -1;

    // ============ Методы, перенесённые из ядра (recovery-only) ============

    // Точка восстановления E (§2): Failure → ErrorPos; Partial → max(NewPos, ErrorPos);
    // Success до EOF → max(NewPos, ErrorPos): NewPos — хвостовой мусор, ErrorPos — самая дальняя точка падения.
    private int RecoveryPointOf(Result result, string input)
    {
        if (result.TryGetSuccess(out _, out var end))
            return end < input.Length ? Math.Max(end, ErrorPos) : input.Length;
        if (result.TryGetPartial(out _, out var partialEnd))
            return Math.Max(partialEnd, ErrorPos);
        return ErrorPos;
    }

    // Инъекция: Length == 0 — пустой узел-вставка, Length > 0 — абсорбер [pos..pos+Length).
    private Result CreateInjectedResult(Injection injection, int pos)
    {
        var endPos = pos + injection.Length;
        var node = new TerminalNode(injection.NodeKind, pos, endPos, injection.Length, IsRecovery: true, IsAbsorber: injection.IsSkip && injection.Length > 0);
        return Result.Success(node, endPos, endPos);
    }

    // §3.9/2.3: OftenMissed — сахар над TryInsert: кадр несёт RecoveryOptions(TryInsert: [элемент]),
    // чтобы engine (S1, ранг 0) генерировал кандидата вставки терминала в точке восстановления.
    private static RecoveryOptions? OftenMissedOptions(Rule rule) =>
        rule is OftenMissed { Element: Terminal terminal }
            ? new RecoveryOptions { TryInsert = [terminal] }
            : null;

    // ============ Реализации хуков (продолжение) ============

    private partial void InitFollowCalculator(Dictionary<string, Rule[]> rules) => _followCalculator = new FollowSetCalculator(rules);
    private partial void SetMaxParseDepth(int inputLength)
    {
        _parseDepth = 0;
        _maxParseDepth = Profile.MaxParseDepthBase + inputLength * Profile.MaxParseDepthPerChar;
    }
    private partial void ClearInjections() => _injections.Clear();

    private partial bool BeginParseFrame(int startPos)
    {
        _parseDepth++;
        return _parseDepth > _maxParseDepth;
    }
    private partial void EndParseFrame() => _parseDepth--;

    private partial void PushRuleFrame(string ruleName, int minPrecedence, FrameLocation location, RecoveryOptions? options) =>
        _stackFrames.Add(new StackFrame(ruleName, minPrecedence, location, null, options));
    private partial void PopFrame() => _stackFrames.RemoveAt(_stackFrames.Count - 1);
    private partial Result WithFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, RecoveryOptions? options, Func<Result> action)
    {
        PushFrame(location, expected, fallbackRuleName, options);
        try
        {
            return action();
        }
        finally
        {
            PopFrame();
        }
    }

    private void PushFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, RecoveryOptions? options)
    {
        var ruleName = _stackFrames.Count > 0 ? _stackFrames[^1].RuleName : fallbackRuleName;
        var precedence = _stackFrames.Count > 0 ? _stackFrames[^1].Precedence : 0;
        _stackFrames.Add(new StackFrame(ruleName, precedence, location, expected, options));
    }

    private partial Terminal[] ExpectedFor(Rule element) => FirstSets.Get(element, _followCalculator);
    private partial RecoveryOptions? OptionsFor(Rule rule) => rule is RecoveryRule rr ? rr.Options : OftenMissedOptions(rule);
    private partial RecoveryOptions? OftenMissedOptionsFor(Rule rule) => rule is OftenMissed { Element: Terminal terminal } ? new RecoveryOptions { TryInsert = [terminal] } : null;
    private partial Rule[] PrefixesFor(string ruleName, bool isRecoveryPos)
    {
        var tdoppRule = TdoppRules[ruleName];
        return isRecoveryPos ? tdoppRule.RecoveryPrefix : tdoppRule.Prefix;
    }
    private partial RuleWithPrecedence[] PostfixesFor(string ruleName, bool isRecoveryPos)
    {
        var tdoppRule = TdoppRules[ruleName];
        return isRecoveryPos ? tdoppRule.RecoveryPostfix : tdoppRule.Postfix;
    }
    private partial Result? InjectionAt(int pos, Terminal terminal) =>
        terminal.Injectable && _injections.TryGetValue((pos, terminal), out var injection) ? CreateInjectedResult(injection, pos) : null;
    private partial bool AcceptEpsilonMatch(Rule prefix) =>
        prefix is RecoveryRule rc && (rc.Options is null || rc.Options.Recoverable);
    private partial Result? ParseRecoveryRuleType(Rule rule, int startPos, string input) =>
        rule is RecoveryRule r ? ParseAlternative(r.Inner, startPos, input) : null;
    private partial Result? InsertOftenMissed(OftenMissed oftenMissed, int startPos, Result failedResult) =>
        IsRecoveryPosition(startPos)
            ? Result.Success(new TerminalNode(oftenMissed.Kind, startPos, startPos, ContentLength: 0, IsRecovery: true), startPos, failedResult.MaxFailPos)
            : null;
    private partial bool IsRecoveryTerminal(Terminal terminal) => terminal is RecoveryTerminal;
    private partial bool IsRecoveryRule(Rule alt) => alt.GetSubRules<RecoveryTerminal>().Any();
    private partial (Rule[] Prefix, RuleWithPrecedence[] Postfix) BuildRecoveryTdopp(string ruleName, Rule[] alternatives)
    {
        var recoveryPrefix = new List<Rule>();
        var recoveryPostfix = new List<RuleWithPrecedence>();
        foreach (var alt in alternatives)
        {
            if (alt is Seq { Elements: [Ref rule, .. var rest] } && rule.RuleName == ruleName)
            {
                var reqRef = rest.OfType<ReqRef>().FirstOrDefault();
                if (reqRef is { })
                    recoveryPostfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));
                else
                {
                    var precedence = rule is ReqRef x ? x.Precedence : 0;
                    recoveryPostfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), precedence, Right: false));
                }
            }
            else
                recoveryPrefix.Add(alt);
        }
        return (recoveryPrefix.ToArray(), recoveryPostfix.ToArray());
    }

    // Финализация Parse (recovery): стек правил, таблица мемоизации, точка восстановления e, ErrorInfo.
    private partial void FinalizeResult(Result result, string input)
    {
        Log($"--- RULE STACK TRACE ---", LogImportance.High);
        foreach (var frame in _stackFrames)
            Log($"  Rule: {frame.RuleName}, Prec: {frame.Precedence}, Loc: {frame.Location}, Expected: [{string.Join(", ", (frame.Expected ?? Array.Empty<Terminal>()).Select(t => t.Kind))}]", LogImportance.High);
        Log($"------------------------", LogImportance.High);

        var debugInfos = MemoizationVisualazer(input);
        Log($"Parse failed. Memoization table:");
        foreach (var info in debugInfos)
            Log($"    {info.Info}");
        Log($"and of memoization table.");

        var e = RecoveryPointOf(result, input);

        // Финальные состояния §3.6: Success@EOF / Partial@EOF — восстановлено (ErrorInfo = null,
        // дыры описаны RecoveryDiagnostics); иначе — невосстановлено: FatalError в последней точке e.
        var recovered = (result.TryGetSuccess(out _, out var successEnd) && successEnd == input.Length)
            || (result.TryGetPartial(out _, out var partialEnd) && partialEnd == input.Length);
        ErrorInfo = recovered ? null : new FatalError(input, e, Location: input.PositionToLineCol(e), _expected.ToArray());
    }

    // Один parse (без fail-fast): setup recovery-состояния + цикл восстановления (S0 + ленивый Generate).
    private partial Result Recover(string input, string startRule, int currentStartPos)
    {
        _recoveryDiagnostics.Clear();
        _attempts.Clear();
        EngineGenerateCalls = 0;
        RecoveryPasses = 0;
        HygieneRemovals = 0;
        S2ScanPositions = 0;
        S3ScanPositions = 0;
        _specCache.Reset();
        _recoveryPoint = -1;
        _quietZoneEnd = -1;
        _lastSnapshot = null;
        _lastPartial = null;
        _suppressSideEffects = false;

        var ePrev = -1;
        var e = -1;
        var result = default(Result);

        for (var iter = 0; ; iter++)
        {
            RecoveryPasses++;
            Log($"Starting at {currentStartPos} iter={iter} parse for rule '{startRule}' _recoveryPoint={_recoveryPoint}");
            result = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);

            if (result.TryGetSuccess(out _, out var end) && end == input.Length)
                return result; // чистый успех

            e = RecoveryPointOf(result, input);
            if (e <= ePrev)
                break; // глобальный предохранитель (fail-safe)
            ePrev = e;
            if (iter >= MaxRecoveryIterations)
                break;

            var snapshot = FailureSnapshotAt(e);
            Log($"S0 reparse at recovery point {e}, snapshot: {(snapshot?.Pos.ToString() ?? "n/a")}", LogImportance.High);
            _recoveryPoint = e;

            if (!_attempts.TryGetValue(e, out var attempts))
            {
                attempts = new TierBudget();
                _attempts[e] = attempts;
            }

            // S0 (ре-парсинг как есть: Hygiene без патчей) — всегда первый; S1..S5 генерируются лениво (Generate — дорого:
            // спекулятивные parse'ы) и только если S0 не дал прогресса. Порядок проб/акцепта не меняется (S0 и так первый).
            var recoveredThisIteration = false;

            bool TryCandidate(RecoveryCandidate candidate)
            {
                if (attempts.TriedIds.Contains(candidate.Id))
                    return false; // уже пробовали на этой точке — следующий кандидат
                attempts.TriedIds.Add(candidate.Id);
                // A2: бюджеты по тирам (не по кандидатам). S0 (ранг 0) — базовый re-парс, S6 (ранг 6) —
                // гарантированное дно (1.3.2): оба вне под-бюджетов (всегда пробуются). Остальные —
                // под-бюджет своего тира: S1 (много вставок) не выедает общий бюджет → S2/S3/S6 не голодают.
                if (candidate.Rank is not 0 and not 6)
                {
                    var count = candidate.Rank switch
                    {
                        1 => attempts.S1,
                        2 => attempts.S2,
                        _ => attempts.S3S6
                    };
                    var budget = candidate.Rank switch
                    {
                        1 => S1TierBudget,
                        2 => S2TierBudget,
                        _ => S3S6TierBudget
                    };
                    if (count >= budget)
                        return false; // под-бюджет тира исчерпан — следующий кандидат (не стопим цикл)
                }
                switch (candidate.Rank)
                {
                    case 1: attempts.S1++; break;
                    case 2: attempts.S2++; break;
                    case 3:
                    case 4:
                    case 5: attempts.S3S6++; break;
                }

                var log = ApplyPatches(candidate, e, snapshot, startRule, currentStartPos); // патчи + Hygiene атомарно, всё в лог
                var savedErrorPos = ErrorPos;
                var savedExpected = _expected.ToArray();
                _recoveryPoint = e;
                var next = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
                var e2 = RecoveryPointOf(next, input);

                // 1.3.4: S6 (ранг 6) — дно, а не восстановление: ошибка в точке e не «починена», а лишь
                // поглощена до EOF (абсорбер). Отчёт и продолжение (A4-2): отметка Unrecovered в точке
                // восстановления e. Gate — candidate.Rank == 6 (S6 — принятый кандидат): цикл пробует
                // кандидатов по порядку и выходит при первом дающем прогресс, поэтому если принят S6 —
                // ни один ремонтный (S0–S5) прогресса не дал. НЕ бюджетная эвристика (attempts.Count).
                void AddUnrecoveredIfS6()
                {
                    if (candidate.Rank != 6)
                        return;
                    _recoveryDiagnostics.Add(new RecoveryDiagnostic(e, e, RecoveryKind.Unrecovered, $"error at {e} not recovered (absorbed to EOF)", null, startRule));
                }

                if (next.TryGetSuccess(out _, out var end2) && end2 == input.Length)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    AddUnrecoveredIfS6();
                    result = next;
                    _quietZoneEnd = e;
                    recoveredThisIteration = true;
                    return true; // полностью восстановлено
                }
                if (e2 > e)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    AddUnrecoveredIfS6();
                    result = next;
                    _quietZoneEnd = e;
                    recoveredThisIteration = true;
                    return true; // I1: прогресс — к следующему итеративному проходу
                }
                RollbackPatches(log); // нет прогресса — откат (включая Hygiene), следующий кандидат
                ErrorPos = savedErrorPos;
                _expected = new HashSet<Terminal>(savedExpected);
                return false;
            }

            if (!TryCandidate(CandidateS0(e, snapshot, startRule)))
            {
                EngineGenerateCalls++;
                var parseEnd = result.TryGetSuccess(out _, out var pe) ? pe : result.TryGetPartial(out _, out var pp) ? pp : 0;
                foreach (var candidate in Recovery.RecoveryEngine.Generate(e, snapshot, input, this, result.ResultKind, startRule, currentStartPos, parseEnd))
                    if (TryCandidate(candidate))
                        break;
            }

            if (result.TryGetSuccess(out _, out var endAfter) && endAfter == input.Length)
                return result; // полностью восстановлено

            if (!recoveredThisIteration)
                break; // кандидаты исчерпаны — ошибка неотвратима
        }

        return result;
    }

    // Спекулятивный parse: подавление побочных эффектов (снимки/ErrorPos/_expected) на время parse и откат.
    private partial Result Speculative(Func<Result> parse)
    {
        var savedErrorPos = ErrorPos;
        var savedExpected = _expected.ToArray();
        var savedSnapshot = _lastSnapshot;
        _suppressSideEffects = true;
        try
        {
            return parse();
        }
        finally
        {
            _suppressSideEffects = false;
            ErrorPos = savedErrorPos;
            _expected = new HashSet<Terminal>(savedExpected);
            _lastSnapshot = savedSnapshot;
        }
    }

    // ============ MemoPatch-лог (1.3): применение патчей кандидата + Hygiene атомарно, откат по логy ============

    private void BeginPatchLog()
    {
        _memoPatchLog = new List<MemoPatch>();
        _injectionPatchLog = new List<InjectionPatch>();
    }

    private PatchLog EndPatchLog()
    {
        var log = new PatchLog(_memoPatchLog!, _injectionPatchLog!);
        _memoPatchLog = null;
        _injectionPatchLog = null;
        return log;
    }

    private void RecordMemo((int pos, string rule, int precedence) key)
    {
        if (_memoPatchLog is not { } log)
            return;
        var had = _memo.TryGetValue(key, out var old);
        log.Add(new MemoPatch(key, had ? old : null));
    }

    private void RecordMemoRemove((int pos, string rule, int precedence) key)
    {
        if (_memoPatchLog is not { } log)
            return;
        var had = _memo.TryGetValue(key, out var old);
        log.Add(new MemoPatch(key, had ? old : null));
    }

    private void RecordInjection((int pos, Terminal Terminal) key)
    {
        if (_injectionPatchLog is not { } log)
            return;
        var had = _injections.TryGetValue(key, out var old);
        log.Add(new InjectionPatch(key, had ? old : null));
    }

    // Патчи кандидата + Hygiene в одном атомарном логy (всё для отката). Интеграция цикла (1.3).
    // Hygiene ДО Apply: S6-дно (A1) memo-патчит start-правило на currentStartPos — если Hygiene шёл бы после,
    // он удалил бы этот патч (scope b) и ре-парс не увидел бы Success@EOF. S1-S5 патчат другие позиции, для них порядок неважен.
    public PatchLog ApplyPatches(RecoveryCandidate candidate, int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        HygieneCore(e, snapshot, startRule, currentStartPos);
        candidate.Apply(this);
        return EndPatchLog();
    }

    // Hygiene в отдельном логy (для тестов): см. HygieneCore.
    public PatchLog Hygiene(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        HygieneCore(e, snapshot, startRule, currentStartPos);
        return EndPatchLog();
    }

    // Гигиена memo — ТОЧНАЯ (2.1/B1). Удаляются только записи, реально невалидные в точке восстановления E:
    //  (f) Failure с pos < E && MaxFailPos >= E: падение, начавшееся ДО E и дошедшее до E (или дальше) — его
    //      вычисление опиралось на вход в окрестности E, а re-парсинг меняет его инъекцией/абсорбером → невалидно.
    //      Failure с MaxFailPos < E (целиком в доверенном префиксе) и Failure с pos >= E — не трогаем.
    //  (b) start-правило на currentStartPos ЛЮБОГО типа (Partial/Success < EOF): устаревший верхнеуровневый результат
    //      первого прохода — иначе re-парсинг читает его и не переиспытывает start-правило (тесты SeparatedList/ErrorEmpty).
    // Success/Partial никогда не удаляются (I2: валидные факты префикса). Требует активный лог патчей.
    private void HygieneCore(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        for (var p = currentStartPos; p <= e; p++)
        {
            if (!_memoByPos.TryGetValue(p, out var keys))
                continue;
            foreach (var key in keys.ToArray())
            {
                if (!_memo.TryGetValue(key, out var value))
                    continue;
                var isStartRuleAtCurrentStart = key.pos == currentStartPos && key.rule == startRule;
                var isInvalidFailure = value.ResultKind == Result.Kind.Failure && key.pos >= currentStartPos && key.pos <= e;
                if (isStartRuleAtCurrentStart || isInvalidFailure)
                {
                    RemoveMemo(key.rule, key.pos, key.precedence);
                    HygieneRemovals++;
                }
            }
        }
    }

    // Откат логов (обратный порядок): возвращает OldValue (или удаляет ключ, если OldValue == null).
    public void RollbackPatches(PatchLog log)
    {
        for (var i = log.Memo.Count - 1; i >= 0; i--)
        {
            var patch = log.Memo[i];
            if (patch.OldValue is { } old)
            {
                _memo[patch.Key] = old;
                IndexMemoAdd(patch.Key);
            }
            else
            {
                _memo.Remove(patch.Key);
                IndexMemoRemove(patch.Key);
            }
        }

        for (var i = log.Injection.Count - 1; i >= 0; i--)
        {
            var patch = log.Injection[i];
            if (patch.OldValue is { } old)
                _injections[patch.Key] = old;
            else
                _injections.Remove(patch.Key);
        }
    }

    // С0 — неявный кандидат «ре-парсинг как есть»: без патчей (Hygiene применяется в ApplyPatches). Всегда первый.
    private RecoveryCandidate CandidateS0(int e, FailureSnapshot? snapshot, string startRule) =>
        new("S0", 0, e, 0, startRule, null, _ => { }, _ => { }, []);

    // В 1.1 снимок — заготовка для engine'а (1.2): возвращаем _lastSnapshot, если он снят в точке e,
    // иначе null (синтетический снимок для хвостового мусора появится в 1.2/S5).
    private FailureSnapshot? FailureSnapshotAt(int e)
    {
        var snapshot = _lastSnapshot;
        return snapshot is not null && snapshot.Pos == e ? snapshot : null;
    }

    private void CaptureSnapshot(int pos, Terminal failedTerminal)
    {
        var stack = _stackFrames.ToArray();
        var expected = new List<Terminal>();
        if (stack.Length > 0)
        {
            var topExpected = stack[stack.Length - 1].Expected;
            if (topExpected is not null)
                expected.AddRange(topExpected);
        }
        if (!expected.Contains(failedTerminal))
            expected.Add(failedTerminal);
        _lastSnapshot = new FailureSnapshot(pos, stack, failedTerminal, expected.ToArray());
    }

    // A2: состояние под-бюджетов тиров на одной точке восстановления. S0 (ранг 0) и S6 (ранг 6)
    // вне под-бюджетов; счётчики S1/S2/S3S6 — число кандидатов тира, уже пробованных на точке.
    private sealed class TierBudget
    {
        public readonly HashSet<string> TriedIds = new();
        public int S1;
        public int S2;
        public int S3S6;
    }
}
