#nullable enable

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Diagnostics;
using ExtensibleParaser;

namespace ExtensibleParaser;

public record SkippedNode(int StartPos, int EndPos) : Node("Skipped", StartPos, EndPos, true)
{
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
    public override string ToString(string input) => input.Substring(StartPos, EndPos - StartPos);
    public override string ToString() => $"«Skipped: {DebugContent()}»";
}

public class Parser(Terminal trivia, Log? log = null)
{
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA2211 // Non-constant fields should not be visible
    /// <summary>
    /// Используется только для отладки. Позволяет отображать разобранный код в наследниках Node не храня в нем входной строки.
    /// </summary>
    [ThreadStatic]
    [Obsolete("This field should be used for debugging purposes only. Do not use it in the visitor parser itself.")]
    public static string? Input;
#pragma warning restore CA2211 // Non-constant fields should not be visible
#pragma warning restore IDE0079 // Remove unnecessary suppression

    public int ErrorPos { get; private set; }
    public FatalError ErrorInfo { get; private set; }

    private int _recoverySkipPos = -1;
    private FollowSetCalculatorV2? _followCalculator;


    private readonly Stack<string> _ruleStack = new();
    private readonly HashSet<Terminal> _expected = [];
    public Terminal Trivia { get; private set; } = trivia;
    public Log? Logger { get; set; } = log;

    [Conditional("TRACE")]
    private void Log(string message, LogImportance importance = LogImportance.Normal, [CallerMemberName] string? memberName = null, [CallerLineNumber] int line = 0) =>
        Logger?.Info($"{memberName} {line}: {message}", importance);

    public Dictionary<string, Rule[]> Rules { get; } = new();
    public Dictionary<string, TdoppRule> TdoppRules { get; } = new();

    private readonly Dictionary<(int pos, string rule, int precedence), Result> _memo = new();

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
        _followCalculator = new FollowSetCalculatorV2(Rules);
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
                    var reqRef = rest.OfType<ReqRef>().First();
                    recoveryPostfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));

                    if (!isRecoveryRule)
                        postfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));
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
#pragma warning disable CS0618 // Type or member is obsolete
        Input = input;
#pragma warning restore CS0618 // Type or member is obsolete
        triviaLength = 0;
        ErrorPos = startPos;
        _recoverySkipPos = -1;
        var currentStartPos = startPos;
        _memo.Clear();

        if (input.Length > 0)
        {
            Log($"Starting at {currentStartPos} parse for trivia");
            triviaLength = Trivia.TryMatch(input, currentStartPos);
            Guard.IsTrue(triviaLength >= 0);
            currentStartPos += triviaLength;
        }


        Log($"Starting at {currentStartPos} parse for rule '{startRule}'");

        var rootNode = new SeqNode(startRule, new List<ISyntaxNode>(), 0, 0);
        var nodes = new List<ISyntaxNode>();
        int oldErrorPos = -1;

        while (currentStartPos < input.Length)
        {
            oldErrorPos = ErrorPos;
            var result = ParseRule(startRule, 0, currentStartPos, input);
            if (result.TryGetSuccess(out var node, out var newPos))
            {
                nodes.Add(node);
                currentStartPos = newPos;
            }
            else
            {
                var recoveryPos = PanicMode(ErrorPos, input);

                if (recoveryPos > ErrorPos)
                {
                    nodes.Add(new SkippedNode(ErrorPos, recoveryPos));
                    currentStartPos = recoveryPos;
                    ErrorPos = recoveryPos; // Move ErrorPos forward
                }
                else
                {
                    // If panic mode didn't advance, we have to skip at least one char to avoid infinite loop.
                    nodes.Add(new SkippedNode(ErrorPos, ErrorPos + 1));
                    currentStartPos = ErrorPos + 1;
                    ErrorPos++;
                }
            }

            // Clear memoization cache for the recovered region to allow fresh parsing
            foreach (var x in _memo.Keys.Where(k => k.pos >= oldErrorPos).ToList())
            {
                _memo.Remove(x);
            }
        }

        rootNode = rootNode with { Elements = nodes, EndPos = currentStartPos };
        return Result.Success(rootNode, currentStartPos, ErrorPos);
    }

    private Result ParseRule(
        string ruleName,
        int minPrecedence,
        int startPos,
        string input)
    {
        var isRecoveryPos = startPos == _recoverySkipPos;
        var memoKey = (startPos, ruleName, minPrecedence);

        if (_memo.TryGetValue(memoKey, out var cached))
        {
            if (_recoverySkipPos == cached.MaxFailPos)
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

        _ruleStack.Push(ruleName);

        foreach (var prefix in prefixRules)
        {
            Log($"  Trying prefix: {prefix}");
            var prefixResult = ParseAlternative(prefix, startPos, input);

            if (prefixResult.MaxFailPos > maxFailPos)
                maxFailPos = prefixResult.MaxFailPos;

            if (!prefixResult.TryGetSuccess(out var node, out var newPos))
                continue;

            Log($"  Prefix at {newPos} success: {node.Kind} prefixResult: {prefixResult}");
            var postfixResult = ProcessPostfix(tdoppRule, node, minPrecedence, newPos, input);

            if (postfixResult.TryGetSuccess(out var postNode, out var postNewPos))
            {
                Log($"  Postfix at {newPos} to {postNewPos} success: {postNode.Kind}: «{input[newPos..postNewPos]}» full expr at {startPos}: «{input[startPos..postNewPos]}»");
                if (postNewPos > maxPos)
                {
                    maxPos = postNewPos;
                    bestResult = postfixResult;
                }
                else if (postNewPos == maxPos && bestResult == null && isRecoveryPos)
                {
                    // Это if нужен для обработки Error-правил восстанавливающих парсинг в случае недописанных конструкаций
                    // (в которых пропущен терминал). Например, в случае пропущенного подврыважния в "1 + ".
                    // Далее сдесь можно сделать логику разрешения неоднозначностей и более качественная работа с Error-правилами.
                    maxPos = postNewPos;
                    bestResult = postfixResult;
                }
            }
        }

        _ruleStack.Pop();

        if (bestResult is { } result)
            return _memo[memoKey] = result;

        return _memo[memoKey] = Result.Failure(maxFailPos);
    }

    private Result ProcessPostfix(
        TdoppRule rule,
        ISyntaxNode prefixNode,
        int minPrecedence,
        int startPos,
        string input)
    {
        var newPos = startPos;
        var currentResult = prefixNode;
        var maxFailPos = startPos;

        while (true)
        {
            var bestPostfix = (RuleWithPrecedence?)null;
            var bestNode = (ISyntaxNode?)null;
            var bestPos = newPos;
            var isRecoveryPos = newPos == _recoverySkipPos;
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

                if (!result.TryGetSuccess(out var node, out var parsedPos))
                    continue;

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

            if (bestPos == _recoverySkipPos)
            {
                _recoverySkipPos = -1;
                Log($"Rule recovery finished {currentResult}. New pos: {bestPos}");
                break;
            }

            Log($"Postfix at {newPos} [{bestNode}] is preferred. New pos: {bestPos}");
            currentResult = bestNode!;
            newPos = bestPos;
        }

        return Result.Success(currentResult, newPos, maxFailPos);
    }

    private Result TryParsePostfix(
        RuleWithPrecedence postfix,
        ISyntaxNode currentResult,
        int startPos,
        string input)
    {
        var elements = new List<ISyntaxNode> { currentResult };
        var newPos = startPos;
        var maxFailPos = startPos;

        foreach (var element in postfix.Seq.Elements)
        {
            Log($"    Parsing at {newPos} postfix element: {element}");
            var result = ParseAlternative(element, newPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            if (!result.TryGetSuccess(out var node, out var parsedPos))
                return result;

            elements.Add(node);
            newPos = parsedPos;
        }

        return Result.Success(new SeqNode(postfix.Seq.Kind ?? "Seq", elements, startPos, newPos), newPos, maxFailPos);
    }

    private Result ParseAlternative(
        Rule rule,
        int startPos,
        string input) => rule switch
        {
            Terminal t => ParseTerminal(t, startPos, input),
            Seq s => ParseSeq(s, startPos, input),
            OneOrMany o => ParseOneOrMany(o, startPos, input),
            ZeroOrMany z => ParseZeroOrMany(z, startPos, input),
            Ref r => ParseRule(r.RuleName, 0, startPos, input),
            ReqRef r => ParseRule(r.RuleName, r.Precedence, startPos, input),
            Optional o => ParseOptional(o, startPos, input),
            OftenMissed o => ParseOftenMissed(o, startPos, input),
            AndPredicate a => ParseAndPredicate(a, startPos, input),
            NotPredicate n => ParseNotPredicate(n, startPos, input),
            SeparatedList sl => ParseSeparatedList(sl, startPos, input),
            _ => throw new IndexOutOfRangeException($"Unsupported rule type: {rule.GetType().Name}: {rule}")
        };

    private Result ParseAndPredicate(AndPredicate a, int startPos, string input)
    {
        var errorPos = ErrorPos;
        var predicateResult = ParseAlternative(a.PredicateRule, startPos, input);
        ErrorPos = errorPos;
        return predicateResult.IsSuccess
            ? ParseAlternative(a.MainRule, startPos, input)
            : Result.Failure(startPos);
    }

    private Result ParseNotPredicate(NotPredicate predicate, int startPos, string input)
    {
        var errorPos = ErrorPos;
        var predicateResult = ParseAlternative(predicate.PredicateRule, startPos, input);
        ErrorPos = errorPos;
        if (!predicateResult.IsSuccess)
            return ParseAlternative(predicate.MainRule, startPos, input);
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

        if (!result.IsSuccess && startPos == _recoverySkipPos)
            return Result.Success(new TerminalNode(oftenMissed.Kind, startPos, startPos, ContentLength: 0, IsRecovery: true), startPos, result.MaxFailPos);
        return result;
    }

    private Result ParseOneOrMany(OneOrMany oneOrMany, int startPos, string input)
    {
        Log($"Parsing at {startPos} OneOrMany: {oneOrMany}");
        var currentPos = startPos;
        var elements = new List<ISyntaxNode>();

        // Parse at least one element
        var firstResult = ParseAlternative(oneOrMany.Element, currentPos, input);
        if (!firstResult.TryGetSuccess(out var firstNode, out var newPos))
            return Result.Failure(firstResult.MaxFailPos);

        var maxFailPos = firstResult.MaxFailPos;

        elements.Add(firstNode);
        currentPos = newPos;

        // Parse remaining elements
        while (true)
        {
            var result = ParseAlternative(oneOrMany.Element, currentPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            if (!result.TryGetSuccess(out var node, out newPos))
                break;

            elements.Add(node);
            currentPos = newPos;
        }

        return Result.Success(new SeqNode(oneOrMany.Kind ?? "OneOrMany", elements, startPos, currentPos), currentPos, maxFailPos);
    }

    private Result ParseZeroOrMany(ZeroOrMany zeroOrMany, int startPos, string input)
    {
        Log($"Parsing at {startPos} ZeroOrMany: {zeroOrMany}");
        var currentPos = startPos;
        var elements = new List<ISyntaxNode>();
        var maxFailPos = startPos;

        while (true)
        {
            var result = ParseAlternative(zeroOrMany.Element, currentPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            if (!result.TryGetSuccess(out var node, out var newPos))
                break;

            elements.Add(node);
            currentPos = newPos;
        }

        return Result.Success(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos, maxFailPos);
    }

    private static ChatRef Preview(string input, int pos, int len = 5) => pos >= input.Length
        ? "«»"
        : $"«{input.AsSpan(pos, Math.Min(input.Length - pos, len))}»";

    private Result ParseTerminal(Terminal terminal, int startPos, string input)
    {
        //if (startPos == _recoverySkipPos)
        //    return panicRecovery(terminal, startPos, input);

        // Стандартная логика парсинга терминала
        var currentPos = startPos;
        var contentLength = terminal.TryMatch(input, startPos);
        if (contentLength < 0)
        {
            if (startPos >= ErrorPos)
            {
                if (startPos > ErrorPos)
                {
                    _expected.Clear();
                    ErrorPos = startPos;
                }
                _expected.Add(terminal);
            }
            Log($"Terminal mismatch: {terminal.Kind} at {startPos}: {Preview(input, startPos)}");
            return Result.Failure(startPos);
        }

        currentPos += contentLength;

        // Skip trailing trivia
        var triviaLength = Trivia.TryMatch(input, currentPos);
        if (triviaLength > 0)
            currentPos += triviaLength;

        Log($"Matched terminal: {terminal.Kind} at [{startPos}-{startPos + contentLength}) len={contentLength} trivia: [{startPos + contentLength}-{currentPos}) len={triviaLength} «{input.AsSpan(startPos, contentLength)}»");
        return Result.Success(
            new TerminalNode(
                terminal.Kind,
                startPos,
                EndPos: currentPos,
                contentLength,
                IsRecovery: terminal is RecoveryTerminal
            ),
            currentPos,
            maxFailPos: currentPos // ???
        );
    }

    private int PanicMode(int errorPos, string input)
    {
        Log($"Entering Panic Mode at {errorPos}", LogImportance.High);
        var anchors = GetAnchorTerminals();

        // First, check for anchors on new lines for statement-like rules
        var statementStarters = Rules.Values.SelectMany(x => x).Where(r => r.IsStatementLike).ToList();

        var nextPos = errorPos + 1;
        while (nextPos < input.Length)
        {
            var lineEnd = input.IndexOf('\n', nextPos);
            if (lineEnd == -1) lineEnd = input.Length;

            for (int i = nextPos; i < lineEnd; i++)
            {
                // Skip trivia
                var triviaLen = Trivia.TryMatch(input, i);
                if (triviaLen > 0)
                {
                    i += triviaLen -1;
                    continue;
                }

                foreach (var anchor in anchors)
                {
                    if (anchor.TryMatch(input, i) > 0)
                    {
                        Log($"Found anchor '{anchor}' at {i}. Resuming parsing.", LogImportance.High);
                        return i;
                    }
                }
            }
            nextPos = lineEnd + 1;
        }

        Log("Panic mode reached end of input.", LogImportance.High);
        return input.Length; // Reached end of file
    }

    private IReadOnlyCollection<Terminal> GetAnchorTerminals()
    {
        var anchors = new HashSet<Terminal>();
        var followCalculator = Guard.AssertIsNonNull(_followCalculator);

        foreach (var ruleName in _ruleStack)
        {
            anchors.UnionWith(followCalculator.GetFollowSet(ruleName));

            // This part is tricky because we don't have direct access to the rule object here, only its name.
            // We'd need to look up the rule from the name to check if it's a looping construct.
            // For now, let's just use the Follow sets.
        }

        // Also add common statement starters as anchors
        var statementFirsts = followCalculator.GetFirstSet("Statement");
        anchors.UnionWith(statementFirsts);
        var functionFirsts = followCalculator.GetFirstSet("Function");
        anchors.UnionWith(functionFirsts);

        return anchors;
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

        foreach (var element in seq.Elements)
        {
            var result = ParseAlternative(element, newPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            if (!result.TryGetSuccess(out var node, out var parsedPos))
            {
                Log($"Seq element failed: {element} at {newPos}");
                return result;
            }

            elements.Add(node);
            newPos = parsedPos;
        }

        return Result.Success(new SeqNode(seq.Kind ?? "Seq", elements, startPos, newPos), newPos, maxFailPos);
    }

    // пока сделал разделение что было наглядно на ревью, по идее нужно объединить после обсуждения норм или не норм.
    private Result ParseSeparatedList(SeparatedList listRule, int startPos, string input)
    {
        Log($"Parsing at {startPos} SeparatedList: {listRule}");

        var elements = new List<ISyntaxNode>();
        var delimiters = new List<ISyntaxNode>();

        int currentPos = startPos;

        // Первый элемент
        var firstResult = ParseAlternative(listRule.Element, currentPos, input);

        if (!firstResult.TryGetSuccess(out var firstNode, out var newPos))
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

        var maxFailPos = firstResult.MaxFailPos;
        elements.Add(firstNode);
        currentPos = newPos;

        // Последующие элементы
        while (true)
        {
            // Парсинг разделителя
            var sepResult = ParseAlternative(listRule.Separator, currentPos, input);
            if (sepResult.MaxFailPos > maxFailPos)
                maxFailPos = sepResult.MaxFailPos;

            if (!sepResult.TryGetSuccess(out var sepNode, out newPos))
            {
                if (listRule.EndBehavior == SeparatorEndBehavior.Required)
                {
                    Log($"Missing separator at {currentPos}.");
                    return Result.Failure(maxFailPos);
                }

                break;
            }

            // Добавляем разделитель
            delimiters.Add(sepNode);
            currentPos = newPos;

            // Парсинг элемента после разделителя
            var elemResult = ParseAlternative(listRule.Element, currentPos, input);
            if (elemResult.MaxFailPos > maxFailPos)
                maxFailPos = elemResult.MaxFailPos;

            if (!elemResult.TryGetSuccess(out var elemNode, out newPos))
            {
                if (listRule.EndBehavior == SeparatorEndBehavior.Forbidden)
                {
                    Log($"End sepearator should not be present {currentPos}.");
                    return Result.Failure(maxFailPos);
                }

                break;
            }

            elements.Add(elemNode);
            currentPos = newPos;
        }

        if (listRule.EndBehavior == SeparatorEndBehavior.Forbidden)
        {
            // Парсинг разделителя
            var sepResult = ParseAlternative(listRule.Separator, currentPos, input);
            if (sepResult.MaxFailPos > maxFailPos)
                maxFailPos = sepResult.MaxFailPos;

            if (sepResult.TryGetSuccess(out var sepNode, out newPos))
            {
                Log($"End sepearator should not be present {currentPos}.");
                return Result.Failure(maxFailPos);
            }
        }

        return Result.Success(
            new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos),
            newPos: currentPos,
            maxFailPos: maxFailPos
        );
    }
}
