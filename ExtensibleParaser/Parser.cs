#nullable enable

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ExtensibleParaser;

public class Parser
{
    public bool EnableLogging { get; set; }
    public int ErrorPos { get; private set; }
    public bool IsRecoveryMode { get; private set; }

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

        foreach (var kv in _memo.OrderBy(x => x.Key.pos).ThenBy(x => x.Value.Length).ThenBy(x => x.Key.precedence).ThenByDescending(x => x.Value.IsSuccess))
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
        triviaLength = 0;
        _memo.Clear();
        ErrorPos = startPos;
        IsRecoveryMode = false;

        if (Trivia != null && input.Length > 0)
        {
            Log($"Starting at {startPos} parse for trivia ({Trivia})");
            triviaLength = Trivia.TryMatch(input, startPos);
            Guard.IsTrue(triviaLength >= 0);
            startPos += triviaLength;
        }

        Log($"Starting at {startPos} parse for rule '{startRule}'");
        var result = ParseRule(startRule, minPrecedence: 0, currentPos: startPos, input);

        if (EnableLogging)
        {
            Log("Memoization table:");
            foreach (var entry in MemoizationVisualazer(input))
                Log("    " + entry.Info);
            Log("end of memoization table.");
        }

        if (!result.TryGetSuccess(out _, out var newPos) || newPos != input.Length)
        {
            //_memo.Clear();
            //IsRecoveryMode = true;
            //result = ParseRule(startRule, minPrecedence: 0, currentPos: startPos, input);

            return Result.Failure($"Unexpected characters at position {ErrorPos}. Expected: {string.Join(", ", _expected.OrderBy(x => x.ToString()))}");
        }

        return result;
    }

    private Result ParseRule(
        string ruleName,
        int minPrecedence,
        int currentPos,
        string input)
    {
        var memoKey = (currentPos, ruleName, minPrecedence);
        if (_memo.TryGetValue(memoKey, out var cached))
        {
            Log($"Memo hit: {memoKey} => {cached}");
            return cached;
        }

        if (!TdoppRules.TryGetValue(ruleName, out var tdoppRule))
            return Result.Failure($"Rule {ruleName} not found");

        Log($"Processing at {currentPos} rule: {ruleName} Prefixs: [{string.Join<Rule>(", ", tdoppRule.Prefix)}]");
        Result? bestResult = null;
        int maxPos = currentPos;

        foreach (var prefix in tdoppRule.Prefix)
        {
            Log($"  Trying prefix: {prefix}");
            var prefixResult = ParseAlternative(prefix, currentPos, input);
            if (!prefixResult.TryGetSuccess(out var node, out var newPos))
                continue;

            Log($"  Prefix at {newPos} success: {node.Kind} prefixResult: {prefixResult}");
            var postfixResult = ProcessPostfix(tdoppRule, node, minPrecedence, newPos, input);

            if (postfixResult.TryGetSuccess(out var postNode, out var postNewPos))
            {
                Log($"  Postfix at {newPos} to {postNewPos} success: {postNode.Kind}: «{input[newPos..postNewPos]}» full expr at {currentPos}: «{input[currentPos..postNewPos]}»");
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

        if (bestResult != null)
        {
            _memo[memoKey] = bestResult.Value;
            return bestResult.Value;
        }

        _memo[memoKey] = Result.Failure("No alternatives matched");
        return _memo[memoKey];
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
                if (parsedPos > bestPos || parsedPos == bestPos && !postfix.Right && bestPostfix!.Right)
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
        int currentPos,
        string input)
    {
        var elements = new List<ISyntaxNode> { lhs };
        int newPos = currentPos;

        foreach (var element in postfix.Seq.Elements)
        {
            Log($"    Parsing at {newPos} postfix element: {element}");
            var result = ParseAlternative(element, newPos, input);
            if (!result.TryGetSuccess(out var node, out var parsedPos))
                return Result.Failure("Postfix element failed");

            elements.Add(node);
            newPos = parsedPos;
        }

        return Result.Success(new SeqNode(postfix.Seq.Kind ?? "Seq", elements, currentPos, newPos), newPos);
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
            return Result.Success(new SomeNode(optional.Kind ?? "Optional", node, currentPos, newPos),newPos);

        return Result.Success(new NoneNode(optional.Kind ?? "Optional", currentPos, currentPos),currentPos);
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

    private Result ParseTerminal(
        Terminal terminal,
        int startPos,
        string input)
    {
        Log($"Parsing at {startPos} terminal: {terminal}");

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

        var triviaLength = 0;

        // Skip trailing trivia
        var trivia = Trivia;
        if (trivia != null && startPos < input.Length)
        {
            triviaLength = trivia.TryMatch(input, currentPos);
            Guard.IsTrue(triviaLength >= 0);
            if (triviaLength > 0)
                currentPos += triviaLength;
        }

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
