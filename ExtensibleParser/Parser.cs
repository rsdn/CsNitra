#nullable enable

using Diagnostics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExtensibleParser;

using ExtensibleParser.Recovery;

public class Parser(Terminal trivia, Log? log = null)
{
    [ThreadStatic]
    private static string? _debugInput;

    /// <summary>
    /// Используется только для отладки. Позволяет отображать разобранный код в наследниках Node не храня в нем входной строки.
    /// </summary>
    [Obsolete("This field should be used for debugging purposes only. Do not use it in the visitor parser itself.")]
    public static string? Input => _debugInput;

    internal static string? DebugInput => _debugInput;

    public int ErrorPos { get; private set; }
    public FatalError? ErrorInfo { get; private set; }

    private int _recoveryPoint = -1;
    private FollowSetCalculator? _followCalculator;


    private readonly List<StackFrame> _stackFrames = [];

    public IReadOnlyList<StackFrame> CurrentStackFrames => _stackFrames;
    private HashSet<Terminal> _expected = [];
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
    public int MaxRecoveryAttemptsPerPosition { get; set; } = 3;
    private readonly Dictionary<int, HashSet<string>> _attempts = new();

    // 3.0a: guard глубины рекурсивного парсинга — предохранитель от stack overflow во время recovery re-parse.
    // _parseDepth — текущая вложенность ParseAlternative (incr на входе, decr в finally); _maxParseDepth — лимит.
    // Дефолт 4096 безопасен для спекулятивных парсеров, которые вызывают ParseRule напрямую (не через Parse),
    // поэтому у них лимит не обнуляется; Parse уточняет его как функцию длины входа.
    private int _parseDepth;
    private int _maxParseDepth = 4096;

    // Лог патчей текущего кандидата (MemoPatch): заполняется хуками, пока активен, — основа отката.
    private List<Recovery.MemoPatch>? _patchLog;

    // Хук для тестов/engine: инжекции в Фазе 0 никто не порождает, слой активен с Фазы 1.
    public void AddInjection(Terminal terminal, int pos, Injection injection) => _injections[(pos, terminal)] = injection;

    // Хуки engine (1.2): применение/откат инъекций с сохранением старого значения.
    public IReadOnlyDictionary<(int Pos, Terminal Terminal), Injection> Injections => _injections;
    public void ApplyInjection(Terminal terminal, int pos, Injection injection)
    {
        var key = (pos, terminal);
        RecordInjection(key, injection);
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
        RecordMemo(key, value);
        _memo[key] = value;
    }

    public void RemoveMemo(string rule, int pos, int precedence)
    {
        var key = (pos, rule, precedence);
        RecordMemoRemove(key);
        _memo.Remove(key);
    }

    // Патч для всех прецедентов (e, rule, prec'), присутствующих в memo (TDOPP, §3.4 S3).
    public void PatchMemo(string rule, int pos, Result value)
    {
        foreach (var key in _memo.Keys.Where(k => k.pos == pos && k.rule == rule).ToList())
        {
            RecordMemo(key, value);
            _memo[key] = value;
        }
    }

    public FollowSetCalculator? FollowCalculator => _followCalculator;
    public Terminal[] GetTerminators(IReadOnlyList<StackFrame> stack) =>
        _followCalculator?.GetTerminators(stack) ?? [EofTerminal.Instance];

    // Одноразовый parse правила без recovery-цикла (спекулятивная валидация engine'а, §3.4 S2).
    public Result ParseRuleOnce(string ruleName, int minPrecedence, int startPos, string input) =>
        ParseRule(ruleName, minPrecedence, startPos, input);

    // ============ MemoPatch-лог (1.3): применение патчей кандидата + Hygiene атомарно, откат по логy ============

    private void BeginPatchLog() => _patchLog = new List<Recovery.MemoPatch>();

    private List<Recovery.MemoPatch> EndPatchLog()
    {
        var log = _patchLog!;
        _patchLog = null;
        return log;
    }

    private void RecordMemo((int pos, string rule, int precedence) key, Result value)
    {
        if (_patchLog is not { } log)
            return;
        var had = _memo.TryGetValue(key, out var old);
        log.Add(new Recovery.MemoPatch(_memo, key, had ? (object)old : null, value));
    }

    private void RecordMemoRemove((int pos, string rule, int precedence) key)
    {
        if (_patchLog is not { } log)
            return;
        _memo.TryGetValue(key, out var old);
        log.Add(new Recovery.MemoPatch(_memo, key, old, null));
    }

    private void RecordInjection((int pos, Terminal Terminal) key, Recovery.Injection value)
    {
        if (_patchLog is not { } log)
            return;
        var had = _injections.TryGetValue(key, out var old);
        log.Add(new Recovery.MemoPatch(_injections, key, had ? (object)old : null, value));
    }

    // Патчи кандидата + Hygiene в одном атомарном логy (всё для отката). Интеграция цикла (1.3).
    public List<Recovery.MemoPatch> ApplyPatches(Recovery.RecoveryCandidate candidate, int e, Recovery.FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        candidate.Apply(this);
        HygieneCore(e, snapshot, startRule, currentStartPos);
        return EndPatchLog();
    }

    // Hygiene в отдельном логy (для тестов): см. HygieneCore.
    public List<Recovery.MemoPatch> Hygiene(int e, Recovery.FailureSnapshot? snapshot, string startRule, int currentStartPos)
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
    // I2 нарушается только для Failure в префиксе [currentStartPos, e) и для записи start-правила — не удаётся избежать. Требует активный _patchLog.
    private void HygieneCore(int e, Recovery.FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        foreach (var key in _memo.Keys.ToList())
            if (key.pos == currentStartPos && key.rule == startRule || _memo[key].ResultKind == Result.Kind.Failure)
                RemoveMemo(key.rule, key.pos, key.precedence);
    }

    // Откат логa (обратный порядок): возвращает OldValue (или удаляет ключ, если OldValue == null).
    public void RollbackPatches(List<Recovery.MemoPatch> log)
    {
        for (var i = log.Count - 1; i >= 0; i--)
        {
            var patch = log[i];
            if (patch.Table == _memo)
            {
                var key = (ValueTuple<int, string, int>)patch.Key;
                if (patch.OldValue is { } old)
                {
                    _memo[key] = (Result)old;
                }
                else
                {
                    _memo.Remove(key);
                }
            }
            else
            {
                var key = (ValueTuple<int, Terminal>)patch.Key;
                if (patch.OldValue is { } old)
                    _injections[key] = (Recovery.Injection)old;
                else
                    _injections.Remove(key);
            }
        }
    }

    // С0 — неявный кандидат «ре-парсинг как есть»: без патчей (Hygiene применяется в ApplyPatches). Всегда первый.
    private Recovery.RecoveryCandidate CandidateS0(int e, Recovery.FailureSnapshot? snapshot, string startRule) =>
        new("S0", 0, e, 0, startRule, null, _ => { }, _ => { }, []);

    public Terminal Trivia { get; private set; } = trivia;
    public Log? Logger { get; set; } = log;

    [Conditional("TRACE")]
    private void Log(string message, LogImportance importance = LogImportance.Normal, [CallerMemberName] string? memberName = null, [CallerLineNumber] int line = 0) =>
        Logger?.Info($"{memberName} {line}: {message}", importance);

    public Dictionary<string, Rule[]> Rules { get; } = new();
    public Dictionary<string, TdoppRule> TdoppRules { get; } = new();

    private readonly Dictionary<(int pos, string rule, int precedence), Result> _memo = new();

    // Чистый кэш результата TryMatch (length >= 0 или -1); mismatch кэшируется и никогда не чистится в ходе прохода.
    private readonly Dictionary<(int Pos, Terminal Terminal), int> _terminalCache = new(TerminalComparer.KeyComparer);
    // Слой инъекций engine'а: проверяется ПЕРЕД _terminalCache.
    private readonly Dictionary<(int Pos, Terminal Terminal), Injection> _injections = new(TerminalComparer.KeyComparer);

    public void BuildTdoppRules()
    {
        var inlineableRules = TdoppRules
            .Where(kvp => kvp.Value.Postfix.Length == 0 && kvp.Value.Prefix.Length == 1)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Prefix[0]);

        foreach (var ruleName in Rules.Keys.ToList())
            Rules[ruleName] = Rules[ruleName]
                .Select(r => r.InlineReferences(inlineableRules))
                .ToArray();

        BuildTdoppRulesInternal();
        _followCalculator = new FollowSetCalculator(Rules);
    }

    private void BuildTdoppRulesInternal()
    {
        foreach (var kvp in Rules)
        {
            var ruleName = kvp.Key;
            var alternatives = kvp.Value;
            var prefix = new List<Rule>();
            var postfix = new List<RuleWithPrecedence>();
            var recoveryPrefix = new List<Rule>();
            var recoveryPostfix = new List<RuleWithPrecedence>();

            foreach (var alt in alternatives)
            {
                bool isRecoveryRule = alt.GetSubRules<RecoveryTerminal>().Any();

                if (alt is Seq { Elements: [Ref rule, .. var rest] } && rule.RuleName == ruleName)
                {
                    var reqRef = rest.OfType<ReqRef>().FirstOrDefault();
                    if (reqRef is { })
                    {
                        recoveryPostfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));

                        if (!isRecoveryRule)
                            postfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));
                    }
                    else
                    {
                        var precedence = rule is ReqRef x ? x.Precedence : 0;
                        recoveryPostfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), precedence, Right: false));

                        if (!isRecoveryRule)
                            postfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), precedence, Right: false));
                    }
                }
                else
                {
                    recoveryPrefix.Add(alt);

                    if (!isRecoveryRule)
                        prefix.Add(alt);
                }
            }

            TdoppRules[ruleName] = new TdoppRule(
                new Ref(ruleName),
                Kind: ruleName,
                prefix.ToArray(),
                postfix.ToArray(),
                recoveryPrefix.ToArray(),
                recoveryPostfix.ToArray()
            );
        }
    }

    public IReadOnlyList<(string Info, Result Result)> MemoizationVisualazer(string input)
    {
        var results = new List<(string Info, Result Result)>();

        var vaxNodeLen = _memo.Count == 0 ? 0 : _memo.Max(x => x.Value.TryGetSuccess(out var r) ? r.Node.GetType().Name.Length : 0) + 1;
        var vaxKindLen = _memo.Count == 0 ? 0 : _memo.Max(x => x.Value.TryGetSuccess(out var r) ? r.Node.Kind.Length : 0) + 1;

        foreach (var kv in _memo.OrderBy(x => x.Key.pos).ThenBy(x => x.Value.NewPos).ThenBy(x => x.Key.precedence).ThenByDescending(x => x.Value.IsSuccess))
        {
            var builder = new StringBuilder();
            var prec = kv.Key.precedence;
            var precStr = prec > 0 ? $" {prec} precedence " : null;
            var startPos = kv.Key.pos;
            var ruleName = kv.Key.rule;
            string item;

            if (kv.Value.TryGetSuccess(out var node, out var newPos))
            {
                int len = newPos - startPos;
                item = $"[{startPos}..{newPos}) {len} {ruleName} {$"«{input.Substring(startPos, len)}»".PadRight(input.Length + 3)} Node: {node.GetType().Name.PadRight(vaxNodeLen)} Kind: {node.Kind.PadRight(vaxKindLen)} Rule: {ruleName}";
            }
            else
                item = $"Failed {ruleName} at {startPos}{precStr}: {kv.Value.GetError()}";

            results.Add((item, kv.Value));
        }

        return results;
    }

    public Result Parse(string input, string startRule, out int triviaLength, int startPos = 0)
    {
        _debugInput = input;
        ErrorInfo = null;
        triviaLength = 0;
        ErrorPos = startPos;
        _recoveryPoint = -1;
        _lastSnapshot = null;
        _lastPartial = null;
        _suppressSideEffects = false;
        var currentStartPos = startPos;
        _memo.Clear();
        _terminalCache.Clear();
        _injections.Clear();
        _recoveryDiagnostics.Clear();
        _attempts.Clear();
        EngineGenerateCalls = 0;
        RecoveryPasses = 0;
        _parseDepth = 0;
        _maxParseDepth = input.Length * 4 + 128;

        if (input.Length > 0)
        {
            Log($"Starting at {currentStartPos} parse for trivia");
            triviaLength = Trivia.TryMatch(input, currentStartPos);
            Guard.IsTrue(triviaLength >= 0);
            currentStartPos += triviaLength;
        }


        Log($"Starting at {currentStartPos} parse for rule '{startRule}'");

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

            bool TryCandidate(Recovery.RecoveryCandidate candidate)
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

        // Recovery in recovery rules mode failed.
        // Выводим стек правил с метаинформацией
        Log($"--- RULE STACK TRACE ---", LogImportance.High);
        foreach (var frame in _stackFrames)
            Log($"  Rule: {frame.RuleName}, Prec: {frame.Precedence}, Loc: {frame.Location}, Expected: [{string.Join(", ", (frame.Expected ?? Array.Empty<Terminal>()).Select(t => t.Kind))}]", LogImportance.High);
        Log($"------------------------", LogImportance.High);

        var debugInfos = MemoizationVisualazer(input);

        Log($"Parse failed. Memoization table:");
        foreach (var info in debugInfos)
            Log($"    {info.Info}");
        Log($"and of memoization table.");

        // Финальные состояния §3.6: Success@EOF / Partial@EOF — восстановлено (ErrorInfo = null,
        // дыры описаны накопленными RecoveryDiagnostics); иначе — невосстановлено: FatalError
        // в последней неотвратимой точке (последняя точка восстановления e).
        var recovered = (result.TryGetSuccess(out _, out var successEnd) && successEnd == input.Length)
            || (result.TryGetPartial(out _, out var partialEnd) && partialEnd == input.Length);
        ErrorInfo = recovered ? null : new FatalError(input, e, Location: input.PositionToLineCol(e), _expected.ToArray());
        return result;
    }

    // Точка восстановления E (§2): Failure → ErrorPos; Partial → max(NewPos, ErrorPos);
    // Success до EOF → max(NewPos, ErrorPos): NewPos — хвостовой мусор, ErrorPos — самая дальняя точка падения
    // (для дегенеративных Success'ов, где разбор «сдался» раньше реальной ошибки).
    private int RecoveryPointOf(Result result, string input)
    {
        if (result.TryGetSuccess(out _, out var end))
            return end < input.Length ? Math.Max(end, ErrorPos) : input.Length;
        if (result.TryGetPartial(out _, out var partialEnd))
            return Math.Max(partialEnd, ErrorPos);
        return ErrorPos;
    }

    // В 1.1 снимок — заготовка для engine'а (1.2): возвращаем _lastSnapshot, если он снят в точке e,
    // иначе null (синтетический снимок для хвостового мусора появится в 1.2/S5).
    private FailureSnapshot? FailureSnapshotAt(int e)
    {
        var snapshot = _lastSnapshot;
        return snapshot is not null && snapshot.Pos == e ? snapshot : null;
    }

    private Result ParseRule(
        string ruleName,
        int minPrecedence,
        int startPos,
        string input)
    {
        var isRecoveryPos = startPos == _recoveryPoint;
        var memoKey = (startPos, ruleName, minPrecedence);

        if (_memo.TryGetValue(memoKey, out var cached))
        {
            if (cached.ResultKind == Result.Kind.Partial && _recoveryPoint == cached.MaxFailPos)
                Log($"Ignoring possible partial memo in recovery mode: {memoKey} => {cached}");
            else if (_recoveryPoint == cached.MaxFailPos)
                Log($"Ignoring posible failed memo in recovery mode: {memoKey} => {cached}");
            else
            {
                Log($"Memo hit: {memoKey} => {cached}");
                return cached;
            }
        }

        if (!TdoppRules.TryGetValue(ruleName, out var tdoppRule))
            throw new InvalidDataException($"The rule '{ruleName}' does not exist. Existing rules: [{TdoppRules.Keys.OrderBy(x => x)}].");

        if (isRecoveryPos)
            Log($"Recover at {startPos} rule: {ruleName} Prefixs: [{string.Join<Rule>(", ", tdoppRule.Prefix)}]", LogImportance.High);
        else
            Log($"Processing at {startPos} rule: {ruleName} Prefixs: [{string.Join<Rule>(", ", tdoppRule.Prefix)}]");

        var bestResult = (Result?)null;
        var maxPos = startPos;
        var prefixRules = isRecoveryPos ? tdoppRule.RecoveryPrefix : tdoppRule.Prefix;
        var maxFailPos = startPos;

        for (var altIdx = 0; altIdx < prefixRules.Length; altIdx++)
        {
            var prefix = prefixRules[altIdx];
            var altOptions = prefix is RecoveryRule rr ? rr.Options : OftenMissedOptions(prefix);
            _stackFrames.Add(new StackFrame(ruleName, minPrecedence, new RuleFrameLocation(altIdx), null, altOptions));
            try
            {
                Log($"  Trying prefix: {prefix}");
                var prefixResult = ParseAlternative(prefix, startPos, input);

                if (prefixResult.MaxFailPos > maxFailPos)
                    maxFailPos = prefixResult.MaxFailPos;

                // Базовый случай Partial (ParseSeq): Partial доступен через LastPartial
                if (prefixResult.ResultKind == Result.Kind.Partial)
                {
                    Log($"  Partial result at {startPos}: {prefixResult.Node?.Kind}", LogImportance.High);
                    continue;
                }

                // Try success first, then partial (partial continues parsing but marks as incomplete)
                bool gotSuccess = prefixResult.TryGetSuccess(out var node, out var newPos);
                bool gotPartial = !gotSuccess && prefixResult.TryGetPartial(out node, out newPos);

                if (!gotSuccess && !gotPartial)
                    continue;

                node = node.AssertIsNonNull();
                Log($"  Prefix at {newPos} success: {node.Kind} prefixResult: {prefixResult}");
                var postfixResult = ContinueFromPartialPostfix(tdoppRule, node, minPrecedence, newPos, input);

                // If prefix was partial, propagate partial status
                if (gotPartial && postfixResult.ResultKind != Result.Kind.Partial)
                {
                    var propCtx = new ParseContext(ruleName, new RuleFrameLocation(altIdx), FirstSets.Get(prefix, _followCalculator), null);
                    postfixResult = Result.Partial(postfixResult.Node!, postfixResult.NewPos, postfixResult.MaxFailPos, propCtx);
                }

                if (postfixResult.TryGetSuccess(out var postNode, out var postNewPos) || postfixResult.TryGetPartial(out postNode, out postNewPos))
                {
                    Log($"  Postfix at {newPos} to {postNewPos} success: {postNode.Kind}: «{input[newPos..postNewPos]}» full expr at {startPos}: «{input[startPos..postNewPos]}»");
                    if (postNewPos > maxPos)
                    {
                        maxPos = postNewPos;
                        bestResult = postfixResult;
                    }
                    else if (postNewPos == maxPos && bestResult is { ResultKind: Result.Kind.Partial } && postfixResult.ResultKind == Result.Kind.Success)
                    {
                        // Тай-брейк longest-match: при равной длине Success выигрывает у Partial
                        maxPos = postNewPos;
                        bestResult = postfixResult;
                    }
                    else if (postNewPos == maxPos && bestResult == null && isRecoveryPos
                             && prefix is RecoveryRule rc && (rc.Options is null || rc.Options.Recoverable))
                    {
                        // ε-совпадение Error-правила (пропущенный операнд/аргумент): нулевой прогресс принимается,
                        // но ТОЛЬКО если альтернатива аннотирована RecoveryRule (Recoverable, по умолчанию true).
                        // Триггер — аннотация автора, а не глобальный флаг isRecoveryPos (замена хака 0.3B, §1.5).
                        maxPos = postNewPos;
                        bestResult = postfixResult;
                    }
                }
            }
            finally
            {
                PopFrame();
            }
        }

        if (bestResult is { } result)
        {
            RecordMemo(memoKey, result);
            _memo[memoKey] = result;
            return result;
        }

        var failure = Result.Failure(maxFailPos);
        RecordMemo(memoKey, failure);
        _memo[memoKey] = failure;
        return failure;
    }

    private Result ContinueFromPartialPostfix(
        TdoppRule rule,
        ISyntaxNode prefixNode,
        int minPrecedence,
        int startPos,
        string input)
    {
        var newPos = startPos;
        var currentResult = prefixNode;
        var maxFailPos = startPos;
        var isPartial = false;

        while (true)
        {
            var bestPostfix = (RuleWithPrecedence?)null;
            var bestNode = (ISyntaxNode?)null;
            var bestPos = newPos;
            var isRecoveryPos = newPos == _recoveryPoint;
            var postfixRules = isRecoveryPos ? rule.RecoveryPostfix : rule.Postfix;

            foreach (var postfix in postfixRules)
            {
                // Проверяем, что постфикс применим с учетом приоритета и ассоциативности
                bool isApplicable = postfix.Precedence > minPrecedence || postfix.Precedence == minPrecedence && postfix.Right;

                if (!isApplicable)
                    continue;

                if (isRecoveryPos)
                    Log($"  Trying recovery at {newPos} postfix: {postfix.Seq}", LogImportance.High);
                else
                    Log($"  Trying at {newPos} postfix: {postfix.Seq}");

                var result = TryParsePostfix(postfix, currentResult, newPos, input);

                if (result.MaxFailPos > maxFailPos)
                    maxFailPos = result.MaxFailPos;

                // Try success first, then partial
                bool gotSuccess = result.TryGetSuccess(out var node, out var parsedPos);
                bool gotPartial = !gotSuccess && result.TryGetPartial(out node, out parsedPos);

                if (!gotSuccess && !gotPartial)
                    continue;

                if (gotPartial)
                    isPartial = true;

                // Выбираем самый длинный или самый левый вариант
                if (parsedPos > bestPos || parsedPos == bestPos && (bestPostfix == null || !postfix.Right && bestPostfix!.Right))
                {
                    bestPostfix = postfix;
                    bestPos = parsedPos;
                    bestNode = node;
                }
            }

            if (bestPostfix == null)
                break;

            if (bestPos == _recoveryPoint)
            {
                _recoveryPoint = -1;
                Log($"Rule recovery finished {currentResult}. New pos: {bestPos}");
                break;
            }

            // Guard: нулевой прогресс (ε-постфикс/инъекция не сдвинули позицию) — иначе бесконечный цикл.
            // На легитимных разборах постфикс-оператор всегда съедает ≥1 символ, guard не срабатывает;
            // в recovery с ε-вставками останавливает зацикливание.
            if (bestPos <= newPos)
            {
                Log($"ContinueFromPartialPostfix: цикл остановлен: нулевой прогресс at {newPos}", LogImportance.High);
                break;
            }

            Log($"Postfix at {newPos} [{bestNode}] is preferred. New pos: {bestPos}");
            currentResult = bestNode!;
            newPos = bestPos;
        }

        if (isPartial)
        {
            var postfixCtx = new ParseContext(rule.Kind, new SeqFrameLocation(0), [], null);
            return Result.Partial(currentResult, newPos, maxFailPos, postfixCtx);
        }
        return Result.Success(currentResult, newPos, maxFailPos);
    }

    private Result TryParsePostfix(
        RuleWithPrecedence postfix,
        ISyntaxNode currentResult,
        int startPos,
        string input)
    {
        var newPos = startPos;
        var maxFailPos = startPos;
        List<ISyntaxNode>? elements = null;
        var isPartial = false;

        for (var elemIdx = 0; elemIdx < postfix.Seq.Elements.Length; elemIdx++)
        {
            var element = postfix.Seq.Elements[elemIdx];
            Log($"    Parsing at {newPos} postfix element: {element}");
            var result = WithFrame(new PostfixFrameLocation(elemIdx), FirstSets.Get(element, _followCalculator), postfix.Seq.Kind ?? "Seq",
                () => ParseAlternative(element, newPos, input));

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            bool gotSuccess = result.TryGetSuccess(out var node, out var parsedPos);
            bool gotPartial = !gotSuccess && result.TryGetPartial(out node, out parsedPos);

            if (!gotSuccess && !gotPartial)
                return result;

            if (gotPartial)
                isPartial = true;

            node = node.AssertIsNonNull();
            // Skip predicate nodes as they are not part of the AST
            if (node is not PredicateNode)
            {
                if (elements is null)
                    elements = new List<ISyntaxNode> { currentResult, node };
                else
                    elements.Add(node);
            }

            newPos = parsedPos;
        }

        // Optimization: if there is only one element (currentResult), return it directly instead of wrapping in SeqNode
        if (elements!.Count == 1)
        {
            if (isPartial)
            {
                var pCtx = new ParseContext(postfix.Seq.Kind ?? "Seq", new SeqFrameLocation(postfix.Seq.Elements.Length), [], null);
                return Result.Partial(elements[0], newPos, maxFailPos, pCtx);
            }
            return Result.Success(elements[0], newPos, maxFailPos);
        }

        var seqNode = new SeqNode(postfix.Seq.Kind ?? "Seq", elements, currentResult.StartPos, newPos);
        if (isPartial)
        {
            var pCtx = new ParseContext(postfix.Seq.Kind ?? "Seq", new SeqFrameLocation(postfix.Seq.Elements.Length), [], null);
            return Result.Partial(seqNode, newPos, maxFailPos, pCtx);
        }
        return Result.Success(seqNode, newPos, maxFailPos);
    }

    private Result ParseAlternative(
        Rule rule,
        int startPos,
        string input)
    {
        // 3.0a: guard глубины рекурсии — единая точка: все рекурсивные правила (Seq/Ref/циклы/предикаты)
        // проходят через ParseAlternative. При превышении лимита ветка абортится (Failure), чтобы не
        // уронить процесс (stack overflow) во время recovery re-parse. На легитимных разборах не срабатывает.
        _parseDepth++;
        if (_parseDepth > _maxParseDepth)
        {
            _parseDepth--;
            return Result.Failure(startPos);
        }
        try
        {
            return rule switch
            {
                Terminal t => ParseTerminal(t, startPos, input),
                Seq s => ParseSeq(s, startPos, input),
                OneOrMany o => ParseOneOrMany(o, startPos, input),
                ZeroOrMany z => ParseZeroOrMany(z, startPos, input),
                ReqRef r => ParseRule(r.RuleName, r.Precedence, startPos, input),
                Ref r => ParseRule(r.RuleName, 0, startPos, input),
                Optional o => ParseOptional(o, startPos, input),
                OftenMissed o => ParseOftenMissed(o, startPos, input),
                RecoveryRule r => ParseAlternative(r.Inner, startPos, input),
                AndPredicate a => ParseAndPredicate(a, startPos, input),
                NotPredicate n => ParseNotPredicate(n, startPos, input),
                SeparatedList sl => ParseSeparatedList(sl, startPos, input),
                _ => throw new IndexOutOfRangeException($"Unsupported rule type: {rule.GetType().Name}: {rule}")
            };
        }
        finally
        {
            _parseDepth--;
        }
    }

    private Result ParseAndPredicate(AndPredicate a, int startPos, string input)
    {
        var predicateResult = Speculative(() => ParseAlternative(a.PredicateRule, startPos, input));
        if (predicateResult.IsSuccess)
            return Result.Success(new PredicateNode(a.Kind, startPos, startPos), startPos, predicateResult.MaxFailPos);
        else
            return Result.Failure(startPos);
    }

    private Result ParseNotPredicate(NotPredicate predicate, int startPos, string input)
    {
        var predicateResult = Speculative(() => ParseAlternative(predicate.PredicateRule, startPos, input));
        if (!predicateResult.IsSuccess)
            return Result.Success(new PredicateNode(predicate.Kind, startPos, startPos), startPos, predicateResult.MaxFailPos);
        else
            return Result.Failure(startPos);
    }

    private Result ParseOptional(Optional optional, int startPos, string input)
    {
        var result = ParseAlternative(optional.Element, startPos, input);

        if (result.TryGetSuccess(out var node, out var newPos))
            return Result.Success(new SomeNode(optional.Kind ?? "Optional", node, startPos, newPos), newPos, result.MaxFailPos);

        return Result.Success(new NoneNode(optional.Kind ?? "Optional", startPos, startPos), startPos, result.MaxFailPos);
    }

    // §3.9/2.3: OftenMissed — документированный сахар над TryInsert: кадр элемента/альтернативы несёт
    // RecoveryOptions(TryInsert: [элемент]), чтобы engine (S1, ранг 0) тоже генерировал кандидата вставки
    // терминала в точке восстановления (идемпотентно с инлайновой вставкой в recovery-позиции).
    private static RecoveryOptions? OftenMissedOptions(Rule rule) =>
        rule is OftenMissed { Element: Terminal terminal }
            ? new RecoveryOptions { TryInsert = [terminal] }
            : null;

    private Result ParseOftenMissed(OftenMissed oftenMissed, int startPos, string input)
    {
        var result = ParseAlternative(oftenMissed.Element, startPos, input);

        if (!result.IsSuccess && startPos == _recoveryPoint)
            return Result.Success(new TerminalNode(oftenMissed.Kind, startPos, startPos, ContentLength: 0, IsRecovery: true), startPos, result.MaxFailPos);
        return result;
    }

    private Result ParseOneOrMany(OneOrMany oneOrMany, int startPos, string input)
    {
        Log($"Parsing at {startPos} OneOrMany: {oneOrMany}");
        var currentPos = startPos;
        var elements = new List<ISyntaxNode>();

        // Parse at least one element
        var firstResult = WithFrame(new LoopFrameLocation("OneOrMany", 0), FirstSets.Get(oneOrMany.Element, _followCalculator), oneOrMany.Kind ?? "OneOrMany",
            () => ParseAlternative(oneOrMany.Element, currentPos, input));

        // Try success first, then partial
        bool gotSuccess = firstResult.TryGetSuccess(out var firstNode, out var newPos);
        bool gotPartial = !gotSuccess && firstResult.TryGetPartial(out firstNode, out newPos);

        if (!gotSuccess && !gotPartial)
            return Result.Failure(firstResult.MaxFailPos);

        firstNode = firstNode.AssertIsNonNull();
        var maxFailPos = firstResult.MaxFailPos;
        var isPartial = gotPartial;

        elements.Add(firstNode);
        currentPos = newPos;

        // Parse remaining elements
        int iteration = 1;
        while (true)
        {
            var result = WithFrame(new LoopFrameLocation("OneOrMany", iteration), FirstSets.Get(oneOrMany.Element, _followCalculator), oneOrMany.Kind ?? "OneOrMany",
                () => ParseAlternative(oneOrMany.Element, currentPos, input));

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            gotSuccess = result.TryGetSuccess(out var node, out newPos);
            gotPartial = !gotSuccess && result.TryGetPartial(out node, out newPos);

            if (!gotSuccess && !gotPartial)
                break;

            node = node.AssertIsNonNull();
            // Guard: zero-width (epsilon) match makes no progress — stop to avoid an infinite loop
            if (newPos == currentPos)
                break;

            if (gotPartial)
                isPartial = true;

            elements.Add(node);
            currentPos = newPos;
            iteration++;
        }

        var loopCtx = isPartial ? new ParseContext(
                oneOrMany.Kind ?? "OneOrMany",
                new LoopFrameLocation("OneOrMany", iteration),
                FirstSets.Get(oneOrMany.Element, _followCalculator),
                null)
            : null;

        if (isPartial)
            return Result.Partial(new SeqNode(oneOrMany.Kind ?? "OneOrMany", elements, startPos, currentPos), currentPos, maxFailPos, loopCtx);
        return Result.Success(new SeqNode(oneOrMany.Kind ?? "OneOrMany", elements, startPos, currentPos), currentPos, maxFailPos);
    }

    private Result ParseZeroOrMany(ZeroOrMany zeroOrMany, int startPos, string input)
    {
        Log($"Parsing at {startPos} ZeroOrMany: {zeroOrMany}");
        var currentPos = startPos;
        var elements = new List<ISyntaxNode>();
        var maxFailPos = startPos;
        var isPartial = false;

        int iteration = 0;
        while (true)
        {
            var result = WithFrame(new LoopFrameLocation("ZeroOrMany", iteration), FirstSets.Get(zeroOrMany.Element, _followCalculator), zeroOrMany.Kind ?? "ZeroOrMany",
                () => ParseAlternative(zeroOrMany.Element, currentPos, input));

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            bool gotSuccess = result.TryGetSuccess(out var node, out var newPos);
            bool gotPartial = !gotSuccess && result.TryGetPartial(out node, out newPos);

            if (!gotSuccess && !gotPartial)
                break;

            node = node.AssertIsNonNull();
            // Guard: zero-width (epsilon) match makes no progress — stop to avoid an infinite loop
            if (newPos == currentPos)
                break;

            if (gotPartial)
                isPartial = true;

            elements.Add(node);
            currentPos = newPos;
            iteration++;
        }

        var loopCtx = isPartial ? new ParseContext(
                zeroOrMany.Kind ?? "ZeroOrMany",
                new LoopFrameLocation("ZeroOrMany", iteration),
                FirstSets.Get(zeroOrMany.Element, _followCalculator),
                null)
            : null;

        if (isPartial)
            return Result.Partial(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos, maxFailPos, loopCtx);
        return Result.Success(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos, maxFailPos);
    }

    private static string Preview(string input, int pos, int len = 5) => pos >= input.Length
        ? "«»"
        : $"«{input.AsSpan(pos, Math.Min(input.Length - pos, len)).Str()}»";

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

    private T Speculative<T>(Func<T> parse)
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

    private Result ParseTerminal(Terminal terminal, int startPos, string input)
    {
        if (_injections.TryGetValue((startPos, terminal), out var injection))
            return CreateInjectedResult(injection, startPos);

        if (!_terminalCache.TryGetValue((startPos, terminal), out var contentLength))
        {
            contentLength = terminal.TryMatch(input, startPos);
            _terminalCache[(startPos, terminal)] = contentLength;
        }

        if (contentLength < 0)
        {
            ReportMismatch(terminal, startPos);
            Log($"Terminal mismatch: {terminal.Kind} at {startPos}: {Preview(input, startPos)}");
            return Result.Failure(startPos);
        }

        var currentPos = startPos + contentLength;

        // Skip trailing trivia
        var triviaLength = Trivia.TryMatch(input, currentPos);
        if (triviaLength > 0)
            currentPos += triviaLength;

        Log($"Matched terminal: {terminal.Kind} at [{startPos}-{startPos + contentLength}) len={contentLength} trivia: [{startPos + contentLength}-{currentPos}) len={triviaLength} «{input.AsSpan(startPos, contentLength).Str()}»");
        return Result.Success(
            new TerminalNode(
                terminal.Kind,
                startPos,
                EndPos: currentPos,
                contentLength,
                IsRecovery: terminal is RecoveryTerminal
            ),
            currentPos,
            maxFailPos: currentPos
        );
    }

    // Протокол «самой дальней точки падения»: вызывается ВСЕГДА при mismatch (и на cache hit, и на miss).
    private void ReportMismatch(Terminal terminal, int pos)
    {
        if (_suppressSideEffects)
            return;
        if (pos >= ErrorPos)
        {
            if (pos > ErrorPos)
            {
                _expected.Clear();
                ErrorPos = pos;
                CaptureSnapshot(pos, terminal);
            }
            _expected.Add(terminal);
        }
    }

    // Инъекция: Length == 0 — пустой узел-вставка, Length > 0 — абсорбер [pos..pos+Length).
    private Result CreateInjectedResult(Injection injection, int pos)
    {
        var endPos = pos + injection.Length;
        var node = new TerminalNode(injection.NodeKind, pos, endPos, injection.Length, IsRecovery: true);
        return Result.Success(node, endPos, endPos);
    }

    private Result ParseSeq(
        Seq seq,
        int startPos,
        string input)
    {
        Log($"Parsing at {startPos} Seq: {seq}");
        var currentPos = startPos;
        var elements = new List<ISyntaxNode>();
        var newPos = currentPos;
        var maxFailPos = startPos;
        var isPartial = false;

        for (int elemIdx = 0; elemIdx < seq.Elements.Length; elemIdx++)
        {
            var element = seq.Elements[elemIdx];
            var result = WithFrame(new SeqFrameLocation(elemIdx), FirstSets.Get(element, _followCalculator), seq.Kind ?? "Seq",
                () => ParseAlternative(element, newPos, input), OftenMissedOptions(element));

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            bool gotSuccess = result.TryGetSuccess(out var node, out var parsedPos);
            bool gotPartial = !gotSuccess && result.TryGetPartial(out node, out parsedPos);

            if (!gotSuccess && !gotPartial)
            {
                Log($"Seq element failed: {element} at {newPos}");
                if (elemIdx > 0 && newPos > startPos)
                {
                    var partialResult = Result.Partial(BuildPartialSeqNode(seq, elements, startPos, newPos), newPos, result.MaxFailPos,
                        new ParseContext(seq.Kind ?? "Seq", new SeqFrameLocation(elemIdx), FirstSets.Get(element, _followCalculator), null));
                    _lastPartial = partialResult;
                    return partialResult;
                }
                return result;
            }

            if (gotPartial)
                isPartial = true;

            node = node.AssertIsNonNull();
            // Skip predicate nodes as they are not part of the AST
            if (node is not PredicateNode)
                elements.Add(node);

            newPos = parsedPos;
        }

        var seqCtx = isPartial ? new ParseContext(
                seq.Kind ?? "Seq",
                new SeqFrameLocation(seq.Elements.Length),
                [],
                null)
            : null;

        // Optimization: if there is only one element, return it directly instead of wrapping in SeqNode
        if (elements.Count == 1)
        {
            if (isPartial)
                return Result.Partial(elements[0], newPos, maxFailPos, seqCtx);
            return Result.Success(elements[0], newPos, maxFailPos);
        }

        var seqNode = new SeqNode(seq.Kind ?? "Seq", elements, startPos, newPos);
        if (isPartial)
            return Result.Partial(seqNode, newPos, maxFailPos, seqCtx);
        return Result.Success(seqNode, newPos, maxFailPos);
    }

    private ISyntaxNode BuildPartialSeqNode(Seq seq, List<ISyntaxNode> elements, int startPos, int endPos)
        => elements.Count == 1
            ? elements[0]
            : new SeqNode(seq.Kind ?? "Seq", elements, startPos, endPos);

    private Result WithFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, Func<Result> parse, RecoveryOptions? options = null)
    {
        PushFrame(location, expected, fallbackRuleName, options);
        try
        {
            return parse();
        }
        finally
        {
            PopFrame();
        }
    }

    private void PushFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, RecoveryOptions? options = null)
    {
        var ruleName = _stackFrames.Count > 0 ? _stackFrames[_stackFrames.Count - 1].RuleName : fallbackRuleName;
        var precedence = _stackFrames.Count > 0 ? _stackFrames[_stackFrames.Count - 1].Precedence : 0;
        _stackFrames.Add(new StackFrame(ruleName, precedence, location, expected, options));
    }

    private void PopFrame() => _stackFrames.RemoveAt(_stackFrames.Count - 1);

    private Result ParseSeparatedList(SeparatedList listRule, int startPos, string input)
    {
        Log($"Parsing at {startPos} SeparatedList: {listRule}");

        var elements = new List<ISyntaxNode>();
        var delimiters = new List<ISyntaxNode>();

        int currentPos = startPos;

        // Первый элемент
        var firstResult = ParseAlternative(listRule.Element, currentPos, input);

        // Try success first, then partial
        bool gotSuccess = firstResult.TryGetSuccess(out var firstNode, out var newPos);
        bool gotPartial = !gotSuccess && firstResult.TryGetPartial(out firstNode, out newPos);

        if (!gotSuccess && !gotPartial)
        {
            if (listRule.CanBeEmpty)
            {
                // Обработка пустого списка
                return Result.Success(
                    new ListNode(listRule.Kind, elements, delimiters, startPos, startPos),
                    newPos: startPos,
                    maxFailPos: startPos);
            }

            Log($"SeparatedList: first element required at {currentPos}");
            return Result.Failure(firstResult.MaxFailPos);
        }

        firstNode = firstNode.AssertIsNonNull();
        var maxFailPos = firstResult.MaxFailPos;
        var isPartial = gotPartial;

        elements.Add(firstNode);
        currentPos = newPos;

        // Последующие элементы
        while (true)
        {
            var iterStartPos = currentPos;
            // Парсинг разделителя
            var sepResult = ParseAlternative(listRule.Separator, currentPos, input);
            if (sepResult.MaxFailPos > maxFailPos)
                maxFailPos = sepResult.MaxFailPos;

            // Try success first, then partial
            gotSuccess = sepResult.TryGetSuccess(out var sepNode, out newPos);
            gotPartial = !gotSuccess && sepResult.TryGetPartial(out sepNode, out newPos);

            if (!gotSuccess && !gotPartial)
            {
                if (listRule.EndBehavior == SeparatorEndBehavior.Required)
                {
                    Log($"Missing separator at {currentPos}.");
                    if (isPartial)
                    {
                        var sepRequiredCtx = new ParseContext(listRule.Kind, new SeqFrameLocation(elements.Count), FirstSets.Get(listRule.Separator, _followCalculator), null);
                        return Result.Partial(new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos), currentPos, maxFailPos, sepRequiredCtx);
                    }
                    return Result.Failure(maxFailPos);
                }

                break;
            }

            if (gotPartial)
                isPartial = true;

            sepNode = sepNode.AssertIsNonNull();
            // Добавляем разделитель
            delimiters.Add(sepNode);
            currentPos = newPos;

            // Парсинг элемента после разделителя
            var elemResult = ParseAlternative(listRule.Element, currentPos, input);
            if (elemResult.MaxFailPos > maxFailPos)
                maxFailPos = elemResult.MaxFailPos;

            // Try success first, then partial
            gotSuccess = elemResult.TryGetSuccess(out var elemNode, out newPos);
            gotPartial = !gotSuccess && elemResult.TryGetPartial(out elemNode, out newPos);

            if (!gotSuccess && !gotPartial)
            {
                if (listRule.EndBehavior == SeparatorEndBehavior.Forbidden)
                {
                    Log($"End sepearator should not be present {currentPos}.");
                    if (isPartial)
                    {
                        var sepForbiddenCtx1 = new ParseContext(listRule.Kind, new SeqFrameLocation(elements.Count), [], null);
                        return Result.Partial(new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos), currentPos, maxFailPos, sepForbiddenCtx1);
                    }
                    return Result.Failure(maxFailPos);
                }

                break;
            }

            if (gotPartial)
                isPartial = true;

            elemNode = elemNode.AssertIsNonNull();
            // Guard: нулевой прогресс (ε-элемент/инъекция не сдвинули позицию) — список вырожден, иначе бесконечный цикл
            if (newPos == iterStartPos)
            {
                Log($"SeparatedList: цикл остановлен: нулевой прогресс at {newPos}", LogImportance.High);
                return Result.Failure(maxFailPos);
            }

            elements.Add(elemNode);
            currentPos = newPos;
        }

        if (listRule.EndBehavior == SeparatorEndBehavior.Forbidden)
        {
            var sepResult = Speculative(() => ParseAlternative(listRule.Separator, currentPos, input));
            if (sepResult.MaxFailPos > maxFailPos)
                maxFailPos = sepResult.MaxFailPos;
            if (sepResult.TryGetSuccess(out _, out _))
            {
                Log($"End sepearator should not be present {currentPos}.");
                if (isPartial)
                {
                    var sepForbiddenCtx2 = new ParseContext(listRule.Kind, new SeqFrameLocation(elements.Count), [], null);
                    return Result.Partial(new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos), currentPos, maxFailPos, sepForbiddenCtx2);
                }
                return Result.Failure(maxFailPos);
            }
        }

        var listNode = new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos);
        var listCtxFinal = isPartial ? new ParseContext(listRule.Kind, new SeqFrameLocation(elements.Count), [], null) : null;
        if (isPartial)
            return Result.Partial(listNode, currentPos, maxFailPos, listCtxFinal!);
        return Result.Success(listNode, currentPos, maxFailPos);
    }
}
