#nullable enable

using Diagnostics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExtensibleParser;

using ExtensibleParser.Recovery;

public partial class Parser(Terminal trivia, Log? log = null)
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

    private HashSet<Terminal> _expected = [];

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

    // Контекстный аргумент для контекстно-зависимого парсинга (Repeat с Count = null).
    // Устанавливается/восстанавливается ContextScope (stack-семантика, вложенные скоупы).
    // Является функцией позиции, поэтому ключи мемо/терминального кэша не расширяются.
    [ThreadStatic]
    private static int? _contextCount;

    public static int? ContextCount
    {
        get => _contextCount;
        set => _contextCount = value;
    }

    // Хуки recovery-подсистемы: реализация в Parser.Recovery.cs;
    // в сборке EnableRecovery=false (Parser.NoRecovery.cs) — тривиальные (no-op/константы).
    // Ядро (этот файл) recovery-кода не содержит: только вызовы этих хуков.

    // Точки входа / финализация
    private partial Result Recover(string input, string startRule, int currentStartPos);
    private partial void FinalizeResult(Result result, string input);

    // Состояние: инициализация/сброс
    private partial void InitFollowCalculator(Dictionary<string, Rule[]> rules);
    private partial void SetMaxParseDepth(int inputLength);
    private partial void ClearInjections();

    // Глубина рекурсии (guard от stack overflow)
    private partial bool BeginParseFrame(int startPos);
    private partial void EndParseFrame();

    // Стек кадров (для генерации кандидатов восстановления)
    private partial void PushRuleFrame(string ruleName, int minPrecedence, FrameLocation location, RecoveryOptions? options);
    private partial void PopFrame();
    private partial Result WithFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, RecoveryOptions? options, Func<Result> action);

    // Запросы к recovery-состоянию
    private partial bool IsRecoveryPosition(int pos);
    private partial bool SuppressSideEffects { get; }
    private partial void ResetRecoveryPoint();
    private partial Terminal[] ExpectedFor(Rule element);
    private partial RecoveryOptions? OptionsFor(Rule rule);
    private partial RecoveryOptions? OftenMissedOptionsFor(Rule element);
    private partial Rule[] PrefixesFor(string ruleName, bool isRecoveryPos);
    private partial RuleWithPrecedence[] PostfixesFor(string ruleName, bool isRecoveryPos);
    private partial Result? InjectionAt(int pos, Terminal terminal);
    private partial bool AcceptEpsilonMatch(Rule prefix);
    private partial Result? ParseRecoveryRuleType(Rule rule, int startPos, string input);
    private partial Result? InsertOftenMissed(OftenMissed oftenMissed, int startPos, Result failedResult);
    private partial bool IsRecoveryTerminal(Terminal terminal);

    // Побочные эффекты (снимки/патчи)
    private partial void OnMismatch(Terminal terminal, int pos);
    private partial void OnMemoWritten((int pos, string rule, int precedence) key);
    private partial void OnMemoRemoved((int pos, string rule, int precedence) key);
    private partial void OnInjectionApplied((int Pos, Terminal Terminal) key);
    private partial void OnPartialCaptured(Result partialResult);

    // Спекулятивный parse
    private partial Result Speculative(Func<Result> parse);

    // Построение TDOPP-правил
    private partial bool IsRecoveryRule(Rule alt);
    private partial (Rule[] Prefix, RuleWithPrecedence[] Postfix) BuildRecoveryTdopp(string ruleName, Rule[] alternatives);

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
        InitFollowCalculator(Rules);
    }

    private void BuildTdoppRulesInternal()
    {
        foreach (var kvp in Rules)
        {
            var ruleName = kvp.Key;
            var alternatives = kvp.Value;
            var prefix = new List<Rule>();
            var postfix = new List<RuleWithPrecedence>();

            foreach (var alt in alternatives)
            {
                if (IsRecoveryRule(alt))
                    continue;

                if (alt is Seq { Elements: [Ref rule, .. var rest] } && rule.RuleName == ruleName)
                {
                    var reqRef = rest.OfType<ReqRef>().FirstOrDefault();
                    if (reqRef is { })
                        postfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));
                    else
                    {
                        var precedence = rule is ReqRef x ? x.Precedence : 0;
                        postfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), precedence, Right: false));
                    }
                }
                else
                    prefix.Add(alt);
            }

            var (recoveryPrefix, recoveryPostfix) = BuildRecoveryTdopp(ruleName, alternatives);
            TdoppRules[ruleName] = new TdoppRule(
                new Ref(ruleName),
                Kind: ruleName,
                prefix.ToArray(),
                postfix.ToArray(),
                recoveryPrefix,
                recoveryPostfix
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
        var currentStartPos = startPos;
        _memo.Clear();
        _terminalCache.Clear();
        ClearInjections();
        SetMaxParseDepth(input.Length);

        if (input.Length > 0)
        {
            Log($"Starting at {currentStartPos} parse for trivia");
            triviaLength = Trivia.TryMatch(input, currentStartPos);
            Guard.IsTrue(triviaLength >= 0);
            currentStartPos += triviaLength;
        }

        Log($"Starting at {currentStartPos} parse for rule '{startRule}'");

        var result = Recover(input, startRule, currentStartPos);
        if (result.TryGetSuccess(out _, out var end) && end == input.Length)
            return result;
        FinalizeResult(result, input);
        return result;
    }

    private Result ParseRule(
        string ruleName,
        int minPrecedence,
        int startPos,
        string input)
    {
        var isRecoveryPos = IsRecoveryPosition(startPos);
        var memoKey = (startPos, ruleName, minPrecedence);

        if (_memo.TryGetValue(memoKey, out var cached))
        {
            if (cached.ResultKind == Result.Kind.Partial && IsRecoveryPosition(cached.MaxFailPos))
                Log($"Ignoring possible partial memo in recovery mode: {memoKey} => {cached}");
            else if (IsRecoveryPosition(cached.MaxFailPos))
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
        var prefixRules = PrefixesFor(ruleName, isRecoveryPos);
        var maxFailPos = startPos;

        for (var altIdx = 0; altIdx < prefixRules.Length; altIdx++)
        {
            var prefix = prefixRules[altIdx];
            var altOptions = OptionsFor(prefix);
            PushRuleFrame(ruleName, minPrecedence, new RuleFrameLocation(altIdx), altOptions);
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
                    var propCtx = new ParseContext(ruleName, new RuleFrameLocation(altIdx), ExpectedFor(prefix), null);
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
                    else if (postNewPos == maxPos && bestResult == null && isRecoveryPos && AcceptEpsilonMatch(prefix))
                    {
                        // ε-совпадение Error-правила: нулевой прогресс принимается только для recoverable RecoveryRule.
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
            OnMemoWritten(memoKey);
            _memo[memoKey] = result;
            return result;
        }

        var failure = Result.Failure(maxFailPos);
        OnMemoWritten(memoKey);
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
            var isRecoveryPos = IsRecoveryPosition(newPos);
            var postfixRules = PostfixesFor(rule.Kind, isRecoveryPos);

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

            if (IsRecoveryPosition(bestPos))
            {
                ResetRecoveryPoint();
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
            var result = WithFrame(new PostfixFrameLocation(elemIdx), ExpectedFor(element), postfix.Seq.Kind ?? "Seq",
                null, () => ParseAlternative(element, newPos, input));

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
        if (BeginParseFrame(startPos))
            return Result.Failure(startPos);
        try
        {
            if (ParseRecoveryRuleType(rule, startPos, input) is { } recoveryRuleResult)
                return recoveryRuleResult;
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
                AndPredicate a => ParseAndPredicate(a, startPos, input),
                NotPredicate n => ParseNotPredicate(n, startPos, input),
                SeparatedList sl => ParseSeparatedList(sl, startPos, input),
                Repeat r => ParseRepeat(r, startPos, input),
                ContextScope c => ParseContextScope(c, startPos, input),
                _ => throw new IndexOutOfRangeException($"Unsupported rule type: {rule.GetType().Name}: {rule}")
            };
        }
        finally
        {
            EndParseFrame();
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

    private Result ParseOftenMissed(OftenMissed oftenMissed, int startPos, string input)
    {
        var result = ParseAlternative(oftenMissed.Element, startPos, input);
        if (!result.IsSuccess && InsertOftenMissed(oftenMissed, startPos, result) is { } inserted)
            return inserted;
        return result;
    }

    private Result ParseOneOrMany(OneOrMany oneOrMany, int startPos, string input)
    {
        Log($"Parsing at {startPos} OneOrMany: {oneOrMany}");
        var currentPos = startPos;
        var elements = new List<ISyntaxNode>();

        // Parse at least one element
        var firstResult = WithFrame(new LoopFrameLocation("OneOrMany", 0), ExpectedFor(oneOrMany.Element), oneOrMany.Kind ?? "OneOrMany",
            null, () => ParseAlternative(oneOrMany.Element, currentPos, input));

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
            var result = WithFrame(new LoopFrameLocation("OneOrMany", iteration), ExpectedFor(oneOrMany.Element), oneOrMany.Kind ?? "OneOrMany",
                null, () => ParseAlternative(oneOrMany.Element, currentPos, input));

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
                ExpectedFor(oneOrMany.Element),
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
            var result = WithFrame(new LoopFrameLocation("ZeroOrMany", iteration), ExpectedFor(zeroOrMany.Element), zeroOrMany.Kind ?? "ZeroOrMany",
                null, () => ParseAlternative(zeroOrMany.Element, currentPos, input));

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
                ExpectedFor(zeroOrMany.Element),
                null)
            : null;

        if (isPartial)
            return Result.Partial(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos, maxFailPos, loopCtx);
        return Result.Success(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos, maxFailPos);
    }

    private Result ParseRepeat(Repeat repeat, int startPos, string input)
    {
        // No active context scope (e.g. speculative recovery probes) = cannot match.
        var count = repeat.Count ?? ContextCount;
        if (count is null)
            return Result.Failure(startPos);

        if (count == 0)
            return Result.Success(new TerminalNode(repeat.Kind ?? "Repeat", startPos, startPos, 0), startPos, startPos);

        var currentPos = startPos;
        var maxFailPos = startPos;
        var elements = new List<ISyntaxNode>();

        for (var i = 0; i < count; i++)
        {
            var result = WithFrame(new LoopFrameLocation("Repeat", i), ExpectedFor(repeat.Element), repeat.Kind ?? "Repeat",
                null, () => ParseAlternative(repeat.Element, currentPos, input));
            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            if (!result.TryGetSuccess(out var node, out var newPos))
                return Result.Failure(maxFailPos);

            // Guard: zero-width (epsilon) element cannot satisfy a positive repetition
            if (newPos == currentPos)
                return Result.Failure(maxFailPos);

            node = node.AssertIsNonNull();
            elements.Add(node);
            currentPos = newPos;
        }

        var resultNode = count == 1
            ? elements[0]
            : new SeqNode(repeat.Kind ?? "Repeat", elements, startPos, currentPos);
        return Result.Success(resultNode, currentPos, maxFailPos);
    }

    private Result ParseContextScope(ContextScope scope, int startPos, string input)
    {
        var sourceResult = WithFrame(new SeqFrameLocation(0), ExpectedFor(scope.Source), scope.Kind ?? "ContextScope",
            null, () => ParseAlternative(scope.Source, startPos, input));
        if (!sourceResult.TryGetSuccess(out var sourceNode, out var sourceEnd))
            return sourceResult;

        var count = CountMatchedElements(sourceNode);
        var previous = ContextCount;
        ContextCount = count;
        try
        {
            // §3.9: the scope body is a strict context — its leaves are context-dependent, so the
            // recovery engine cannot soundly repair a failure inside it (hard fail instead).
            var bodyResult = WithFrame(new SeqFrameLocation(1), ExpectedFor(scope.Body), scope.Kind ?? "ContextScope",
                new RecoveryOptions { Recoverable = false }, () => ParseAlternative(scope.Body, sourceEnd, input));
            if (!bodyResult.TryGetSuccess(out var bodyNode, out var bodyEnd))
                return bodyResult;

            var maxFailPos = Math.Max(sourceResult.MaxFailPos, bodyResult.MaxFailPos);
            var node = new SeqNode(scope.Kind ?? "ContextScope", [sourceNode, bodyNode], startPos, bodyEnd);
            return Result.Success(node, bodyEnd, maxFailPos);
        }
        finally
        {
            ContextCount = previous;
        }
    }

    private static int CountMatchedElements(ISyntaxNode node) => node switch
    {
        SeqNode seq => seq.Elements.Count,
        _ => 1
    };

    private static string Preview(string input, int pos, int len = 5) => pos >= input.Length
        ? "«»"
        : $"«{input.AsSpan(pos, Math.Min(input.Length - pos, len)).Str()}»";

    private Result ParseTerminal(Terminal terminal, int startPos, string input)
    {
        if (InjectionAt(startPos, terminal) is { } injected)
            return injected;

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
                IsRecovery: IsRecoveryTerminal(terminal)
            ),
            currentPos,
            maxFailPos: currentPos
        );
    }

    // Протокол «самой дальней точки падения»: вызывается ВСЕГДА при mismatch (и на cache hit, и на miss).
    private void ReportMismatch(Terminal terminal, int pos)
    {
        if (SuppressSideEffects)
            return;
        if (pos >= ErrorPos)
        {
            if (pos > ErrorPos)
            {
                _expected.Clear();
                ErrorPos = pos;
                OnMismatch(terminal, pos);
            }
            _expected.Add(terminal);
        }
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
            var result = WithFrame(new SeqFrameLocation(elemIdx), ExpectedFor(element), seq.Kind ?? "Seq",
                OftenMissedOptionsFor(element), () => ParseAlternative(element, newPos, input));

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
                        new ParseContext(seq.Kind ?? "Seq", new SeqFrameLocation(elemIdx), ExpectedFor(element), null));
                    OnPartialCaptured(partialResult);
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
                        var sepRequiredCtx = new ParseContext(listRule.Kind, new SeqFrameLocation(elements.Count), ExpectedFor(listRule.Separator), null);
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
