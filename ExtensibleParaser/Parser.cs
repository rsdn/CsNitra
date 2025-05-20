#nullable enable

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Diagnostics;

namespace ExtensibleParaser;

public class Parser(Terminal trivia, Log? log = null)
{
#pragma warning disable CA2211 // Non-constant fields should not be visible
    /// <summary>
    /// Используется только для отладки. Позволяет отображать разобранный код в наследниках Node не храня в нем входной строки.
    /// </summary>
    [ThreadStatic]
    [Obsolete("This field should be used for debugging purposes only. Do not use it in the visitor parser itself.")]
    public static string? Input;
#pragma warning restore CA2211 // Non-constant fields should not be visible

    public int ErrorPos { get; private set; }
    private int _recoverySkipPos = -1;
    private FollowSetCalculator? _followCalculator;


    private readonly Stack<string> _ruleStack = new();
    private readonly HashSet<Terminal> _expected = [];
    public Terminal Trivia { get; private set; } = trivia;
    public Log? Logger { get; set; } = log;

    [Conditional("TRACE")]
    private void Log(string message, LogImportance importance = LogImportance.Normal, [CallerMemberName] string? memberName = null, [CallerLineNumber] int line = 0) =>
        Logger?.Info($"{memberName} ({line}): {message}", importance);

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

        for (int i = 0; ; i++)
        {
            var oldErrorPos = ErrorPos;

            Log($"Starting at {currentStartPos} i={i} parse for rule '{startRule}' _recoverySkipPos={_recoverySkipPos}");
            var normalResult = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);

            if (normalResult.TryGetSuccess(out var node, out var newPos) && newPos == input.Length)
                return normalResult;


            if (ErrorPos <= oldErrorPos)
            {
                // Recovery in recovery rules mode failed.
                // TODO: Implement panic mode recovery for this case.

                var debugInfos = MemoizationVisualazer(input);

                Log($"Parse failed. Memoization table:");
                foreach (var info in debugInfos)
                    Log($"    {info.Info}");
                Log($"and of memoization table.");
                Guard.IsTrue(ErrorPos > oldErrorPos, $"ErrorPos ({ErrorPos}) > oldErrorPos ({oldErrorPos})");
            }

            _recoverySkipPos = ErrorPos;

            foreach (var x in _memo.ToArray())
            {
                var pos = x.Key.pos;

                if (!x.Value.IsSuccess)
                    _memo.Remove(x.Key);

                if (pos == currentStartPos)
                    _memo.Remove(x.Key);

                if (x.Value.NewPos == ErrorPos)
                    _memo.Remove(x.Key);
            }
        }
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

    private static ReadOnlySpan<char> Preview(string input, int pos, int len = 5) => pos >= input.Length
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

        Result panicRecovery(Terminal terminal, int startPos, string input)
        {
            Log($"Starting recovery for terminal {terminal.Kind} at position {startPos}");
            var currentRule = _ruleStack.Peek();
            var followSymbols = Guard.AssertIsNonNull(_followCalculator).GetFollowSet(currentRule);

            // Ищем первую подходящую точку восстановления
            for (int pos = _recoverySkipPos; pos < input.Length; pos++)
            {
                // Пропускаем тривиа
                int triviaSkipped = Trivia.TryMatch(input, pos);
                if (triviaSkipped > 0)
                {
                    Log($"Skipping trivia at position {pos}, length {triviaSkipped}");
                    if (pos > startPos)
                    {
                        var endPos = pos + triviaSkipped;
                        var contentLength = pos - startPos;
                        //var resultNode = new TerminalNode(
                        //        Kind: "Error",
                        //        StartPos: startPos,
                        //        EndPos: endPos,
                        //        ContentLength: contentLength,
                        //        IsRecovery: true);
                        //return Result.Success(resultNode, endPos);
                    }

                    pos += triviaSkipped - 1; // -1 т.к. в цикле будет pos++
                    continue;
                }

                // Проверяем текущий терминал
                if (terminal.TryMatch(input, pos) > 0)
                {
                    Log($"Found matching terminal {terminal.Kind} at position {pos}, exiting recovery mode");
                    _recoverySkipPos = -1;
                    return ParseTerminal(terminal, pos, input);
                }

                // Проверяем Follow-символы
                foreach (var followTerm in followSymbols)
                {
                    if (followTerm.TryMatch(input, pos) > 0 && pos > startPos)
                    {
                        Log($"Found follow symbol {followTerm.Kind} at position {pos}, exiting recovery mode");
                        _recoverySkipPos = -1;

                        // Создаем терминал-ошибку для пропущенной части
                        int errorLength = pos - startPos;
                        var resultNode = new TerminalNode(
                                Kind: "Error",
                                StartPos: startPos,
                                EndPos: pos,
                                ContentLength: errorLength,
                                IsRecovery: true
                            );
                        return Result.Success(
                            resultNode,
                            pos,
                            maxFailPos: startPos
                        );
                    }
                }
            }

            Log("Recovery failed: no valid recovery point found");
            return Result.Failure(startPos);
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

    private Result ParseSeparatedList(SeparatedList listRule, int startPos, string input)
    {
        Log($"Parsing at {startPos} SeparatedList: {listRule}");

        var elements = new List<ISyntaxNode>();
        int currentPos = startPos;
        int maxFailPos = startPos;
        var recoveryMode = currentPos == _recoverySkipPos;

        // Обработка пустого списка
        if (listRule.CanBeEmpty)
        {
            var firstElementResult = ParseAlternative(listRule.Element, currentPos, input);
            if (firstElementResult.MaxFailPos > maxFailPos)
                maxFailPos = firstElementResult.MaxFailPos;

            if (!firstElementResult.IsSuccess)
                return Result.Success(new ListNode(listRule.Kind, elements, startPos, currentPos), currentPos, maxFailPos);
        }

        // Первый элемент
        var firstResult = ParseAlternative(listRule.Element, currentPos, input);
        if (firstResult.MaxFailPos > maxFailPos)
            maxFailPos = firstResult.MaxFailPos;

        if (!firstResult.TryGetSuccess(out var firstNode, out currentPos))
        {
            if (listRule.CanBeEmpty)
                return Result.Success(new ListNode(listRule.Kind, elements, startPos, startPos), startPos, maxFailPos);

            Log($"SeparatedList: first element required at {currentPos}");
            return Result.Failure(maxFailPos).WithRecoveryState(ErrorPos);
        }
        elements.Add(firstNode);

        // Последующие элементы
        while (true)
        {
            // Парсинг разделителя
            var sepResult = ParseAlternative(listRule.Separator, currentPos, input);
            if (sepResult.MaxFailPos > maxFailPos)
                maxFailPos = sepResult.MaxFailPos;

            if (!sepResult.TryGetSuccess(out var sepNode, out var sepPos))
            {
                if (listRule.IsSeparatorOptional)
                {
                    // В режиме восстановления пропускаем проверку разделителя
                    if (recoveryMode) break;

                    // Пытаемся найти элемент без разделителя
                    var elemResult1 = ParseAlternative(listRule.Element, currentPos, input);
                    if (elemResult1.MaxFailPos > maxFailPos)
                        maxFailPos = elemResult1.MaxFailPos;

                    if (!elemResult1.TryGetSuccess(out var elemNode1, out _))
                        break;

                    elements.Add(elemNode1);
                }
                else
                {
                    // Восстановление: пропускаем до следующего элемента
                    if (currentPos >= ErrorPos)
                    {
                        _expected.Add(listRule.Separator as Terminal);
                        ErrorPos = currentPos;
                    }
                    Log($"Missing separator at {currentPos}. Recovery mode: {recoveryMode}");
                    return Result.Failure(maxFailPos);
                }
                continue;
            }

            // Парсинг элемента после разделителя
            var elemResult = ParseAlternative(listRule.Element, sepPos, input);
            if (elemResult.MaxFailPos > maxFailPos)
                maxFailPos = elemResult.MaxFailPos;

            if (!elemResult.TryGetSuccess(out var elemNode, out currentPos))
            {
                // Восстановление: создаем ошибку для пропущенного элемента
                if (currentPos >= ErrorPos)
                {
                    _expected.Add(listRule.Element as Terminal);
                    ErrorPos = sepPos;
                }
                var errorNode = new TerminalNode("Error", sepPos, sepPos, 0, IsRecovery: true);
                elements.Add(errorNode);
                Log($"Missing element after separator at {sepPos}");
                return Result.Success(
                    new ListNode(listRule.Kind, elements, startPos, sepPos),
                    sepPos,
                    maxFailPos
                ).WithRecoveryState(ErrorPos);
            }

            elements.Add(sepNode); // Добавляем разделитель как часть AST
            elements.Add(elemNode);
        }

        return Result.Success(
            new ListNode(listRule.Kind, elements, startPos, currentPos),
            currentPos,
            maxFailPos
        );
    }
}

public static class Ext
{
    public static Result WithRecoveryState(this Result result, int errorPos)
    {
        if (result.TryGetSuccess(out var node, out var pos))
            return Result.Success(node, pos, Math.Max(result.MaxFailPos, errorPos));

        return Result.Failure(Math.Max(result.MaxFailPos, errorPos));
    }
}
