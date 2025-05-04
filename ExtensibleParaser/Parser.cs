#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ExtensibleParaser;

public class Parser
{
#pragma warning disable CA2211 // Non-constant fields should not be visible
    /// <summary>
    /// Используется только для отладки. Позволяет отображать разобранный код в наследниках Node не храня в нем входной строки.
    /// </summary>
    [ThreadStatic]
    [Obsolete("This field should be used for debugging purposes only. Do not use it in the visitor parser itself.")]
    public static string? Input;
#pragma warning restore CA2211 // Non-constant fields should not be visible

    public bool EnableLogging { get; set; }
    public int ErrorPos { get; private set; }
    private int _recoverySkipPos = -1;
    private FollowSetCalculator? _followCalculator;

    private readonly Stack<string> _ruleStack = new Stack<string>();
    private readonly HashSet<Terminal> _expected = [];

    private void Log(string message, [CallerMemberName] string? memberName = null, [CallerLineNumber] int line = 0)
    {
        if (EnableLogging)
            Trace.WriteLine($"{memberName} ({line}): {message}");
    }

    public Terminal? Trivia { get; set; }

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

            foreach (var alt in alternatives)
            {
                if (alt is Seq { Elements: [Ref rule, .. var rest] } && rule.RuleName == ruleName)
                {
                    var reqRef = rest.OfType<ReqRef>().First();
                    postfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));
                }
                else
                    prefix.Add(alt);
            }

            TdoppRules[ruleName] = new TdoppRule(new Ref(ruleName), Kind: ruleName, prefix.ToArray(), postfix.ToArray());
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
                item = $"[{startPos}..{newPos}) {len} {$"«{input.Substring(startPos, len)}»".PadRight(input.Length + 3)} Node: {node.GetType().Name.PadRight(vaxNodeLen)} Kind: {node.Kind.PadRight(vaxKindLen)} Rule: {ruleName}";
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

        if (Trivia != null && input.Length > 0)
        {
            Log($"Starting at {currentStartPos} parse for trivia ({Trivia})");
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
                var debugInfos = MemoizationVisualazer(input);

                Log($"Parse failed. Memoization table:");
                foreach (var info in debugInfos)
                    Log($"    {info.Info}");
                Log($"and of memoization table.");
                Guard.IsTrue(ErrorPos > oldErrorPos);
            }

            _recoverySkipPos = ErrorPos;

            foreach (var x in _memo.ToArray())
            {
                var pos = x.Key.pos;

                if ((36, "Expr", 0) == x.Key)
                {
                }

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
        var memoKey = (startPos, ruleName, minPrecedence);
        if (_memo.TryGetValue(memoKey, out var cached))
        {
            Log($"Memo hit: {memoKey} => {cached} _recoverySkipPos={_recoverySkipPos}");
            return cached;
        }

        if (!TdoppRules.TryGetValue(ruleName, out var tdoppRule))
            return Result.Failure($"Rule {ruleName} not found");

        Log($"Processing at {startPos} rule: {ruleName} Prefixs: [{string.Join<Rule>(", ", tdoppRule.Prefix)}]");
        _ruleStack.Push(ruleName);
        Result? bestResult = null;
        var maxPos = startPos;

        foreach (var prefix in tdoppRule.Prefix)
        {
            Log($"  Trying prefix: {prefix}");
            var prefixResult = ParseAlternative(prefix, startPos, input);
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
                else if (postNewPos == maxPos && bestResult == null)
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

        if (bestResult != null)
            return _memo[memoKey] = bestResult.Value;

        return _memo[memoKey] = Result.Failure("Rule not matched");
    }

    private Result ProcessPostfix(
        TdoppRule rule,
        ISyntaxNode lhs,
        int minPrecedence,
        int currentPos,
        string input)
    {
        var newPos = currentPos;
        var currentResult = lhs;

        while (true)
        {
            RuleWithPrecedence? bestPostfix = null;
            int bestPos = newPos;
            ISyntaxNode? bestNode = null;

            foreach (var postfix in rule.Postfix)
            {
                // Проверяем, что постфикс применим с учетом приоритета и ассоциативности
                bool isApplicable = postfix.Precedence > minPrecedence || postfix.Precedence == minPrecedence && postfix.Right;

                if (!isApplicable)
                    continue;

                Log($"  Trying at {newPos} postfix: {postfix.Seq}");
                var result = TryParsePostfix(postfix, currentResult, newPos, input);
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

            Log($"Postfix at {newPos} [{bestNode}] is preferred. New pos: {bestPos}");
            currentResult = bestNode!;
            newPos = bestPos;
        }

        return Result.Success(currentResult, newPos);
    }

    private Result TryParsePostfix(
        RuleWithPrecedence postfix,
        ISyntaxNode lhs,
        int startPos,
        string input)
    {
        var elements = new List<ISyntaxNode> { lhs };
        int newPos = startPos;

        foreach (var element in postfix.Seq.Elements)
        {
            Log($"    Parsing at {newPos} postfix element: {element}");
            var result = ParseAlternative(element, newPos, input);
            if (!result.TryGetSuccess(out var node, out var parsedPos))
                return Result.Failure("Postfix element failed");

            elements.Add(node);
            newPos = parsedPos;
        }

        return Result.Success(new SeqNode(postfix.Seq.Kind ?? "Seq", elements, startPos, newPos), newPos);
    }

    private Result ParseAlternative(
        Rule rule,
        int currentPos,
        string input) => rule switch
        {
            Terminal t => ParseTerminal(t, currentPos, input),
            Seq s => ParseSeq(s, currentPos, input),
            Choice c => ParseChoice(c, currentPos, input),
            OneOrMany o => ParseOneOrMany(o, currentPos, input),
            ZeroOrMany z => ParseZeroOrMany(z, currentPos, input),
            Ref r => ParseRule(r.RuleName, 0, currentPos, input),
            ReqRef r => ParseRule(r.RuleName, r.Precedence, currentPos, input),
            Optional o => ParseOptional(o, currentPos, input),
            _ => throw new IndexOutOfRangeException($"Unsupported rule type: {rule.GetType().Name}: {rule}")
        };

    private Result ParseOptional(Optional optional, int currentPos, string input)
    {
        var result = ParseAlternative(optional.Element, currentPos, input);

        if (result.TryGetSuccess(out var node, out var newPos))
            return Result.Success(new SomeNode(optional.Kind ?? "Optional", node, currentPos, newPos), newPos);

        return Result.Success(new NoneNode(optional.Kind ?? "Optional", currentPos, currentPos), currentPos);
    }

    private Result ParseOneOrMany(OneOrMany oneOrMany, int currentPos, string input)
    {
        Log($"Parsing at {currentPos} OneOrMany: {oneOrMany}");
        var startPos = currentPos;
        var elements = new List<ISyntaxNode>();

        // Parse at least one element
        var firstResult = ParseAlternative(oneOrMany.Element, currentPos, input);
        if (!firstResult.TryGetSuccess(out var firstNode, out var newPos))
            return Result.Failure("OneOrMany: first element failed");

        elements.Add(firstNode);
        currentPos = newPos;

        // Parse remaining elements
        while (true)
        {
            var result = ParseAlternative(oneOrMany.Element, currentPos, input);
            if (!result.TryGetSuccess(out var node, out newPos))
                break;

            elements.Add(node);
            currentPos = newPos;
        }

        return Result.Success(new SeqNode(oneOrMany.Kind ?? "OneOrMany", elements, startPos, currentPos), currentPos);
    }

    private Result ParseZeroOrMany(ZeroOrMany zeroOrMany, int currentPos, string input)
    {
        Log($"Parsing at {currentPos} ZeroOrMany: {zeroOrMany}");
        var startPos = currentPos;
        var elements = new List<ISyntaxNode>();

        while (true)
        {
            var result = ParseAlternative(zeroOrMany.Element, currentPos, input);
            if (!result.TryGetSuccess(out var node, out var newPos))
                break;

            elements.Add(node);
            currentPos = newPos;
        }

        return Result.Success(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos);
    }

    private static ReadOnlySpan<char> Preview(string input, int pos, int len = 5) => pos >= input.Length
        ? "«»"
        : $"«{input.AsSpan(pos, Math.Min(input.Length - pos, len))}»";

    private Result ParseTerminal(Terminal terminal, int startPos, string input)
    {
        if (startPos == _recoverySkipPos)
            return panicRecovery(terminal, startPos, input);

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
            return Result.Failure("Terminal mismatch");
        }

        currentPos += contentLength;

        // Skip trailing trivia
        var triviaLength = Trivia?.TryMatch(input, currentPos) ?? 0;
        if (triviaLength > 0)
            currentPos += triviaLength;

        Log($"Matched terminal: {terminal.Kind} at [{startPos}-{startPos + contentLength}) len={contentLength} trivia: [{startPos + contentLength}-{currentPos}) len={triviaLength} «{input.AsSpan(startPos, contentLength)}»");
        return Result.Success(
            new TerminalNode(
                terminal.Kind,
                startPos,
                EndPos: currentPos,
                contentLength
            ),
            currentPos
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
                int triviaSkipped = Trivia?.TryMatch(input, pos) ?? 0;
                if (triviaSkipped > 0)
                {
                    Log($"Skipping trivia at position {pos}, length {triviaSkipped}");
                    if (pos > startPos)
                    {
                        var endPos = pos + triviaSkipped;
                        var contentLength = pos - startPos;
                        var resultNode = new TerminalNode(
                                Kind: "Error",
                                StartPos: startPos,
                                EndPos: endPos,
                                ContentLength: contentLength);
                        return Result.Success(resultNode, endPos);
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
                                ContentLength: errorLength
                            );
                        return Result.Success(
                            resultNode,
                            pos
                        );
                    }
                }
            }

            Log("Recovery failed: no valid recovery point found");
            return Result.Failure("Recovery failed: no valid recovery point found");
        }
    }

    private Result ParseSeq(
        Seq seq,
        int currentPos,
        string input)
    {
        Log($"Parsing at {currentPos} Seq: {seq}");
        var startPos = currentPos;
        var elements = new List<ISyntaxNode>();
        var newPos = currentPos;

        foreach (var element in seq.Elements)
        {
            var result = ParseAlternative(element, newPos, input);
            if (!result.TryGetSuccess(out var node, out var parsedPos))
            {
                Log($"Seq element failed: {element} at {newPos}");
                return Result.Failure("Sequence element failed");
            }

            elements.Add(node);
            newPos = parsedPos;
        }

        return Result.Success(new SeqNode(seq.Kind ?? "Seq", elements, startPos, newPos), newPos);
    }

    private Result ParseChoice(
        Choice choice,
        int currentPos,
        string input)
    {
        Log($"Parsing at {currentPos} choice: {choice}");
        var maxPos = currentPos;
        ISyntaxNode? bestResult = null;

        foreach (var alt in choice.Alternatives)
        {
            var result = ParseAlternative(alt, currentPos, input);
            if (!result.TryGetSuccess(out var node, out var parsedPos))
                continue;

            if (parsedPos > maxPos || (parsedPos == maxPos && bestResult == null))
            {
                maxPos = parsedPos;
                bestResult = node;
            }
        }

        return bestResult != null
            ? Result.Success(bestResult, maxPos)
            : Result.Failure("All alternatives failed");
    }
}
