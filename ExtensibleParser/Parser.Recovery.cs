#nullable enable

using Diagnostics;

namespace ExtensibleParser;

using ExtensibleParser.Recovery;

#if RECOVERY

// Recovery-подсистема: состояние, хуки и цикл восстановления.
// Без RECOVERY (EnableRecovery=false) активен Parser.NoRecovery.cs.
public partial class Parser
{
    private int _recoveryPoint = -1;
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

    // Лимиты цикла восстановления: предельное число итераций и число попыток кандидатов на одной точке восстановления.
    public int MaxRecoveryIterations { get; set; } = 64;

    // Дефолт 3 — «предохранитель» по плану (§3.1, I3): ограничивает число попыток кандидатов на одной
    // точке восстановления. Глубокие сценарии (E2E: resync/panic/trailing, ранги S2/S3/S5 = 11-я+ попытка)
    // ставят бюджет ЯВНО (parser.MaxRecoveryAttemptsPerPosition = 16) — глобальный подъём дефолта ломает
    // консервативное поведение существующих тестов (загрязнение _expected / восстановление вопреки
    // Recoverable=false), проверено (3.0b). 3.0a гарантирует, что подъём бюджета не даёт краша.
    public int MaxRecoveryAttemptsPerPosition { get; set; } = 3;
    private readonly Dictionary<int, HashSet<string>> _attempts = new();

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
    public void PatchMemo(string rule, int pos, Result value)
    {
        foreach (var key in _memo.Keys.Where(k => k.pos == pos && k.rule == rule).ToList())
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
    private partial void OnMemoWritten((int pos, string rule, int precedence) key) => RecordMemo(key);
    private partial void OnMemoRemoved((int pos, string rule, int precedence) key) => RecordMemoRemove(key);
    private partial void OnInjectionApplied((int Pos, Terminal Terminal) key) => RecordInjection(key);
    private partial void OnPartialCaptured(Result partialResult) => _lastPartial = partialResult;
    private partial bool IsRecoveryPosition(int pos) => pos == _recoveryPoint;
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
        var node = new TerminalNode(injection.NodeKind, pos, endPos, injection.Length, IsRecovery: true);
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
        _maxParseDepth = inputLength * 4 + 128;
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
        _injections.TryGetValue((pos, terminal), out var injection) ? CreateInjectedResult(injection, pos) : null;
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
        _recoveryPoint = -1;
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
                attempts = new HashSet<string>();
                _attempts[e] = attempts;
            }

            // S0 (ре-парсинг как есть: Hygiene без патчей) — всегда первый; S1..S5 генерируются лениво (Generate — дорого:
            // спекулятивные parse'ы) и только если S0 не дал прогресса. Порядок проб/акцепта не меняется (S0 и так первый).
            var recoveredThisIteration = false;

            bool TryCandidate(RecoveryCandidate candidate)
            {
                if (attempts.Contains(candidate.Id))
                    return false; // уже пробовали на этой точке — следующий кандидат
                attempts.Add(candidate.Id);
                if (attempts.Count > MaxRecoveryAttemptsPerPosition)
                    return true; // лимит попыток на точке исчерпан — стоп (не акцепт: recoveredThisIteration не тронут)

                var log = ApplyPatches(candidate, e, snapshot, startRule, currentStartPos); // патчи + Hygiene атомарно, всё в лог
                var savedErrorPos = ErrorPos;
                var savedExpected = _expected.ToArray();
                _recoveryPoint = e;
                var next = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
                var e2 = RecoveryPointOf(next, input);

                if (next.TryGetSuccess(out _, out var end2) && end2 == input.Length)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    result = next;
                    recoveredThisIteration = true;
                    return true; // полностью восстановлено
                }
                if (e2 > e)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    result = next;
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
                foreach (var candidate in Recovery.RecoveryEngine.Generate(e, snapshot, input, this, result.ResultKind))
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
    public PatchLog ApplyPatches(RecoveryCandidate candidate, int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        candidate.Apply(this);
        HygieneCore(e, snapshot, startRule, currentStartPos);
        return EndPatchLog();
    }

    // Hygiene в отдельном логy (для тестов): см. HygieneCore.
    public PatchLog Hygiene(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        HygieneCore(e, snapshot, startRule, currentStartPos);
        return EndPatchLog();
    }

    // Гигиена memo. Узкий вариант спеки §3.5 (только Failure на e для правил снимка + Failure start-правила
    // на currentStartPos) не проходит recovery-тесты (см. чек-лист 1.3): Error-правила/OftenMissed переиспытывают
    // правила, упавшие в первом проходе на РАЗНЫХ позициях, а не только на e. Минимальное обоснованное расширение:
    //  (c) ВСЕ stale Failure (любая позиция) — только Failure, не Success/Partial (I2: Success/Partial — валидные
    //      факты префикса; применённые патчи — Success, заканчивающиеся в e — не трогаем).
    //  (b) start-правило на currentStartPos ЛЮБОГО типа (Partial/Success < EOF): устаревший верхнеуровневый результат
    //      первого прохода — иначе re-парсинг читает его и не переиспытывает start-правило (тесты SeparatedList/ErrorEmpty).
    // (a) Failure на e для правил снимка — подмножество (c).
    // I2 нарушается только для Failure в префиксе [currentStartPos, e) и для записи start-правила — не удаётся избежать. Требует активный лог патчей.
    private void HygieneCore(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        foreach (var key in _memo.Keys.ToList())
            if (key.pos == currentStartPos && key.rule == startRule || _memo[key].ResultKind == Result.Kind.Failure)
                RemoveMemo(key.rule, key.pos, key.precedence);
    }

    // Откат логов (обратный порядок): возвращает OldValue (или удаляет ключ, если OldValue == null).
    public void RollbackPatches(PatchLog log)
    {
        for (var i = log.Memo.Count - 1; i >= 0; i--)
        {
            var patch = log.Memo[i];
            if (patch.OldValue is { } old)
                _memo[patch.Key] = old;
            else
                _memo.Remove(patch.Key);
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
}

#endif
