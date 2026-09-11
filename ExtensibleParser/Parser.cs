#nullable enable

using Diagnostics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExtensibleParser;

using ExtensibleParser.Recovery;

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
    public FatalError? ErrorInfo { get; private set; }

    private int _recoverySkipPos = -1;
    private FollowSetCalculator? _followCalculator;


    // Структура для хранения информации о положении в правиле
    private record struct RuleStackEntry(string RuleName, string? ParentRule, int? SeqIndex, int? AltIndex, int? LoopDepth);

    private readonly Stack<RuleStackEntry> _ruleStack = new();
    private readonly HashSet<Terminal> _expected = [];
    public Terminal Trivia { get; private set; } = trivia;
    public Log? Logger { get; set; } = log;

    [Conditional("TRACE")]
    private void Log(string message, LogImportance importance = LogImportance.Normal, [CallerMemberName] string? memberName = null, [CallerLineNumber] int line = 0) =>
        Logger?.Info($"{memberName} {line}: {message}", importance);

    public Dictionary<string, Rule[]> Rules { get; } = new();
    public Dictionary<string, TdoppRule> TdoppRules { get; } = new();

    private readonly Dictionary<(int pos, string rule, int precedence), Result> _memo = new();
    private readonly Dictionary<(int pos, string rule, int precedence), Result> _partialMemo = new();
    private Result? _partialAccumulated;

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
#pragma warning disable CS0618 // Type or member is obsolete
        Input = input;
#pragma warning restore CS0618 // Type or member is obsolete
        ErrorInfo = null;
        triviaLength = 0;
        ErrorPos = startPos;
        _recoverySkipPos = -1;
        var currentStartPos = startPos;
        _memo.Clear();
        _partialMemo.Clear();

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

            if (normalResult.TryGetSuccess(out _, out var newPos) && newPos == input.Length)
                return normalResult;

            ErrorInfo = new FatalError(input, ErrorPos, Location: input.PositionToLineCol(ErrorPos), _expected.ToArray());

            if (ErrorPos <= oldErrorPos)
            {
                // Recovery in recovery rules mode failed.
                // Выводим стек правил с метаинформацией
                Log($"--- RULE STACK TRACE ---", LogImportance.High);
                foreach (var entry in _ruleStack.Reverse())
                {
                    Log($"  Rule: {entry.RuleName}, Parent: {entry.ParentRule}, SeqIndex: {entry.SeqIndex}, AltIndex: {entry.AltIndex}, LoopDepth: {entry.LoopDepth}", LogImportance.High);
                }
                Log($"------------------------", LogImportance.High);

                var debugInfos = MemoizationVisualazer(input);

                Log($"Parse failed. Memoization table:");
                foreach (var info in debugInfos)
                    Log($"    {info.Info}");
                Log($"and of memoization table.");
                return normalResult;
            }

            _recoverySkipPos = ErrorPos;

            // First recovery iteration: try to continue from partial results
            if (i == 0 && _partialMemo.Count > 0)
            {
                foreach (var kvp in _partialMemo)
                {
                    if (kvp.Value.TryGetPartial(out var partialTree, out var partialPos) && partialPos == ErrorPos)
                    {
                        Log($"Found partial at ErrorPos: {partialTree.Kind}", LogImportance.High);
                        _partialMemo.Clear();
                        var recoveryResult = ContinueFromPartial(kvp.Key.rule, kvp.Value, minPrecedence: 0, startPos: currentStartPos, input);
                        if (recoveryResult.IsSuccess)
                            return recoveryResult;
                        break;
                    }
                }
            }

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

            _partialMemo.Clear();
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
            if (cached.ResultKind == Result.Kind.Partial && _recoverySkipPos == cached.MaxFailPos)
                Log($"Ignoring possible partial memo in recovery mode: {memoKey} => {cached}");
            else if (_recoverySkipPos == cached.MaxFailPos)
                Log($"Ignoring posible failed memo in recovery mode: {memoKey} => {cached}");
            else
            {
                Log($"Memo hit: {memoKey} => {cached}");
                return cached;
            }
        }

        if (!TdoppRules.TryGetValue(ruleName, out var tdoppRule))
            throw new InvalidDataException($"The rule '{ruleName}' does not exist. Existing rules: [{TdoppRules.Keys.OrderBy(x => x)}].");

        // During recovery, try to continue from partial result
        if (isRecoveryPos && _partialMemo.TryGetValue(memoKey, out var partialCached))
        {
            Log($"Found partial memo in recovery mode: {memoKey}", LogImportance.High);
            var recoveryResult = ContinueFromPartial(ruleName, partialCached, minPrecedence, startPos, input);
            if (recoveryResult.IsSuccess)
                return _memo[memoKey] = recoveryResult;
        }

        if (isRecoveryPos)
            Log($"Recover at {startPos} rule: {ruleName} Prefixs: [{string.Join<Rule>(", ", tdoppRule.Prefix)}]", LogImportance.High);
        else
            Log($"Processing at {startPos} rule: {ruleName} Prefixs: [{string.Join<Rule>(", ", tdoppRule.Prefix)}]");

        var bestResult = (Result?)null;
        var maxPos = startPos;
        var prefixRules = isRecoveryPos ? tdoppRule.RecoveryPrefix : tdoppRule.Prefix;
        var maxFailPos = startPos;

        _ruleStack.Push(new RuleStackEntry(ruleName, ParentRule: null, SeqIndex: null, AltIndex: null, LoopDepth: null));

       foreach (var prefix in prefixRules)
            {
                Log($"  Trying prefix: {prefix}");
                var prefixResult = ParseAlternative(prefix, startPos, input);

                if (prefixResult.MaxFailPos > maxFailPos)
                    maxFailPos = prefixResult.MaxFailPos;

                // Check for partial result - store it and treat as failure
                if (prefixResult.ResultKind == Result.Kind.Partial)
                {
                    Log($"  Partial result at {startPos}: {prefixResult.Node?.Kind}", LogImportance.High);
                    var prefixCtx = new ParseContext(
                        ruleName,
                        new PrefixLocation(prefixRules.ToList().IndexOf(prefix)),
                        GetExpectedTerminals(prefix),
                        null);
                    _partialAccumulated = Result.Partial(prefixResult.Node!, prefixResult.NewPos, prefixResult.MaxFailPos, prefixCtx);
                    _partialMemo[memoKey] = _partialAccumulated.Value;
                    continue;
                }

            // Try success first, then partial (partial continues parsing but marks as incomplete)
            bool gotSuccess = prefixResult.TryGetSuccess(out var node, out var newPos);
            bool gotPartial = !gotSuccess && prefixResult.TryGetPartial(out node, out newPos);
            
            if (!gotSuccess && !gotPartial)
                continue;

            Log($"  Prefix at {newPos} success: {node.Kind} prefixResult: {prefixResult}");
            var postfixResult = ContinueFromPartialPostfix(tdoppRule, node, minPrecedence, newPos, input);

            // If prefix was partial, propagate partial status
            if (gotPartial && postfixResult.ResultKind != Result.Kind.Partial)
            {
                var propCtx = new ParseContext(ruleName, new PrefixLocation(prefixRules.ToList().IndexOf(prefix)), GetExpectedTerminals(prefix), null);
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

        if (_partialAccumulated is { } p)
        {
            _partialMemo[memoKey] = p;
            return _memo[memoKey] = Result.Failure(p.MaxFailPos);
        }

        _ruleStack.Pop();

        if (bestResult is { } result)
            return _memo[memoKey] = result;

        return _memo[memoKey] = Result.Failure(maxFailPos);
    }

    /// <summary>
    /// Continues parsing from a partial result using recovery rules.
    /// When we have a partial tree (e.g., parsed left operand but failed on operator),
    /// we try to recover by parsing from the error position using recovery rules,
    /// then merge the recovered content with the partial tree.
    /// </summary>
    private Result ContinueFromPartial(string ruleName, Result partialResult, int minPrecedence, int startPos, string input)
    {
        if (!partialResult.TryGetPartial(out var partialTree, out var partialPos))
            return Result.Failure(partialResult.MaxFailPos);

        if (!TdoppRules.TryGetValue(ruleName, out var tdoppRule))
            return Result.Failure(partialResult.MaxFailPos);

        Log($"ContinueFromPartial: rule={ruleName} partialPos={partialPos} treeKind={partialTree.Kind}", LogImportance.High);
        
        _partialMemo.Clear();
        
        // Try to recover by parsing from the error position using recovery rules.
        // The partial tree contains what was successfully parsed so far.
        // Recovery rules can fill in the missing parts (e.g., missing operators).
        var recoveryResult = TryRecoverFromPartial(ruleName, partialTree, minPrecedence, startPos, input);
        
        if (recoveryResult.TryGetSuccess(out var recoveredNode, out var recoveredPos))
        {
            // Merge the recovered content with the partial tree.
            // We need to combine: partialTree (already parsed) + recovered content (newly recovered).
            var merged = MergeWithPartialTree(partialTree, recoveredNode);
            return Result.Success(merged, recoveredPos, recoveryResult.MaxFailPos);
        }

        // If recovery failed, fall back to continuing with postfix processing
        return ContinueFromPartialPostfixFallback(tdoppRule, partialTree, minPrecedence, partialPos, input);
    }

    /// <summary>
    /// Try to recover by parsing from the error position using recovery rules.
    /// </summary>
    private Result TryRecoverFromPartial(string ruleName, ISyntaxNode partialTree, int minPrecedence, int startPos, string input)
    {
        if (!TdoppRules.TryGetValue(ruleName, out var tdoppRule))
            return Result.Failure(startPos);

        // Save the current recovery skip position
        var savedRecoverySkipPos = _recoverySkipPos;

        // Temporarily set recovery position to partial tree's end position
        // so recovery rules can trigger at the correct position
        _recoverySkipPos = partialTree.EndPos;

        // Try recovery prefixes at the partial position
        foreach (var prefix in tdoppRule.RecoveryPrefix)
        {
            var prefixResult = ParseAlternative(prefix, partialTree.EndPos, input);

            if (prefixResult.TryGetSuccess(out var node, out var newPos))
            {
                Log($"Recovery prefix matched: {node.Kind} at {newPos}", LogImportance.High);
                var recoveryPostfix = ContinueFromPartialPostfix(tdoppRule, node, minPrecedence, newPos, input);
                _recoverySkipPos = savedRecoverySkipPos;
                if (recoveryPostfix.TryGetSuccess(out var postNode, out var postNewPos))
                {
                    return Result.Success(postNode, postNewPos, recoveryPostfix.MaxFailPos);
                }
                continue;
            }

            if (prefixResult.TryGetPartial(out node, out newPos))
            {
                Log($"Recovery partial matched: {node.Kind} at {newPos}", LogImportance.High);
                var recoveryPostfix = ContinueFromPartialPostfix(tdoppRule, node, minPrecedence, newPos, input);
                _recoverySkipPos = savedRecoverySkipPos;
                if (recoveryPostfix.TryGetSuccess(out var postNode, out var postNewPos) ||
                    recoveryPostfix.TryGetPartial(out postNode, out postNewPos))
                {
                    return Result.Success(postNode, postNewPos, recoveryPostfix.MaxFailPos);
                }
            }
        }

        // No recovery prefix matched - restore and return failure
        _recoverySkipPos = savedRecoverySkipPos;
        return Result.Failure(startPos);
    }

    /// <summary>
    /// Fallback: continue from partial tree using only postfix processing (original behavior).
    /// </summary>
    private Result ContinueFromPartialPostfixFallback(TdoppRule rule, ISyntaxNode partialTree, int minPrecedence, int startPos, string input)
    {
        Log($"Continuing from partial tree (fallback) at {startPos}: {partialTree.Kind}", LogImportance.High);
        return ContinueFromPartialPostfix(rule, partialTree, minPrecedence, startPos, input);
    }

    /// <summary>
    /// Merge a partial tree with a recovered node by creating a SeqNode.
    /// The partial tree represents what was already parsed; the recovered node
    /// represents what was recovered via recovery rules.
    /// </summary>
    private ISyntaxNode MergeWithPartialTree(ISyntaxNode partial, ISyntaxNode recovered)
    {
        // If partial is already a SeqNode, append recovered elements
        if (partial is SeqNode existingSeq)
        {
            var newElements = new List<ISyntaxNode>(existingSeq.Elements.Count + 1);
            foreach (var elem in existingSeq.Elements)
                newElements.Add(elem);
            newElements.Add(recovered);
            return new SeqNode(existingSeq.Kind, newElements, existingSeq.StartPos, recovered.EndPos);
        }
        
        // If recovered is a SeqNode, merge its elements with the partial tree
        if (recovered is SeqNode recoveredSeq)
        {
            var newElements = new List<ISyntaxNode>(1 + recoveredSeq.Elements.Count);
            newElements.Add(partial);
            foreach (var elem in recoveredSeq.Elements)
                newElements.Add(elem);
            return new SeqNode("Recovery", newElements, partial.StartPos, recovered.EndPos);
        }
        
        // Otherwise create a new SeqNode with both
        return new SeqNode("Recovery", new List<ISyntaxNode> { partial, recovered }, partial.StartPos, recovered.EndPos);
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

        if (isPartial)
            {
                var postfixCtx = new ParseContext(rule.Kind, new SeqLocation(0), [], null);
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

        foreach (var element in postfix.Seq.Elements)
        {
            Log($"    Parsing at {newPos} postfix element: {element}");
            var result = ParseAlternative(element, newPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            bool gotSuccess = result.TryGetSuccess(out var node, out var parsedPos);
            bool gotPartial = !gotSuccess && result.TryGetPartial(out node, out parsedPos);
            
            if (!gotSuccess && !gotPartial)
                return result;

            if (gotPartial)
                isPartial = true;

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
                var pCtx = new ParseContext(postfix.Seq.Kind ?? "Seq", new SeqLocation(postfix.Seq.Elements.Length), [], null);
                return Result.Partial(elements[0], newPos, maxFailPos, pCtx);
            }
            return Result.Success(elements[0], newPos, maxFailPos);
        }

        var seqNode = new SeqNode(postfix.Seq.Kind ?? "Seq", elements, currentResult.StartPos, newPos);
        if (isPartial)
        {
            var pCtx = new ParseContext(postfix.Seq.Kind ?? "Seq", new SeqLocation(postfix.Seq.Elements.Length), [], null);
            return Result.Partial(seqNode, newPos, maxFailPos, pCtx);
        }
        return Result.Success(seqNode, newPos, maxFailPos);
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
            ReqRef r => ParseRule(r.RuleName, r.Precedence, startPos, input),
            Ref r => ParseRule(r.RuleName, 0, startPos, input),
            Optional o => ParseOptional(o, startPos, input),
            OftenMissed o => ParseOftenMissed(o, startPos, input),
            AndPredicate a => ParseAndPredicate(a, startPos, input),
            NotPredicate n => ParseNotPredicate(n, startPos, input),
            SeparatedList sl => ParseSeparatedList(sl, startPos, input),
            _ => throw new IndexOutOfRangeException($"Unsupported rule type: {rule.GetType().Name}: {rule}")
        };

    private Result ParseAndPredicate(AndPredicate a, int startPos, string input)
    {
        var savedErrorPos = ErrorPos;
        var savedExpected = _expected.ToList();
        var predicateResult = ParseAlternative(a.PredicateRule, startPos, input);
        ErrorPos = savedErrorPos;
        _expected.Clear();
        foreach (var t in savedExpected) _expected.Add(t);
        if (predicateResult.IsSuccess)
            return Result.Success(new PredicateNode(a.Kind, startPos, startPos), startPos, predicateResult.MaxFailPos);
        else
            return Result.Failure(startPos);
    }

    private Result ParseNotPredicate(NotPredicate predicate, int startPos, string input)
    {
        var savedErrorPos = ErrorPos;
        var savedExpected = _expected.ToList();
        var predicateResult = ParseAlternative(predicate.PredicateRule, startPos, input);
        ErrorPos = savedErrorPos;
        _expected.Clear();
        foreach (var t in savedExpected) _expected.Add(t);
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

        // Try success first, then partial
        bool gotSuccess = firstResult.TryGetSuccess(out var firstNode, out var newPos);
        bool gotPartial = !gotSuccess && firstResult.TryGetPartial(out firstNode, out newPos);

        if (!gotSuccess && !gotPartial)
            return Result.Failure(firstResult.MaxFailPos);

        var maxFailPos = firstResult.MaxFailPos;
        var isPartial = gotPartial;

        elements.Add(firstNode);
        currentPos = newPos;

        // Parse remaining elements
        int iteration = 1;
        while (true)
        {
            var result = ParseAlternative(oneOrMany.Element, currentPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            gotSuccess = result.TryGetSuccess(out var node, out newPos);
            gotPartial = !gotSuccess && result.TryGetPartial(out node, out newPos);

            if (!gotSuccess && !gotPartial)
                break;

            if (gotPartial)
                isPartial = true;

            elements.Add(node);
            currentPos = newPos;
            iteration++;
        }

        var loopCtx = isPartial ? new ParseContext(
                oneOrMany.Kind ?? "OneOrMany",
                new LoopLocation("OneOrMany", iteration),
                GetExpectedTerminals(oneOrMany.Element),
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
            var result = ParseAlternative(zeroOrMany.Element, currentPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            bool gotSuccess = result.TryGetSuccess(out var node, out var newPos);
            bool gotPartial = !gotSuccess && result.TryGetPartial(out node, out newPos);

            if (!gotSuccess && !gotPartial)
                break;

            if (gotPartial)
                isPartial = true;

            elements.Add(node);
            currentPos = newPos;
            iteration++;
        }

        var loopCtx = isPartial ? new ParseContext(
                zeroOrMany.Kind ?? "ZeroOrMany",
                new LoopLocation("ZeroOrMany", iteration),
                GetExpectedTerminals(zeroOrMany.Element),
                null)
            : null;

        if (isPartial)
            return Result.Partial(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos, maxFailPos, loopCtx);
        return Result.Success(new SeqNode(zeroOrMany.Kind ?? "ZeroOrMany", elements, startPos, currentPos), currentPos, maxFailPos);
    }

    private static string Preview(string input, int pos, int len = 5) => pos >= input.Length
        ? "«»"
        : $"«{input.AsSpan(pos, Math.Min(input.Length - pos, len)).Str()}»";

    private Result ParseTerminal(Terminal terminal, int startPos, string input)
    {
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
            maxFailPos: currentPos // ???
        );
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
            var result = ParseAlternative(element, newPos, input);

            if (result.MaxFailPos > maxFailPos)
                maxFailPos = result.MaxFailPos;

            // Try success first, then partial
            bool gotSuccess = result.TryGetSuccess(out var node, out var parsedPos);
            bool gotPartial = !gotSuccess && result.TryGetPartial(out node, out parsedPos);

            if (!gotSuccess && !gotPartial)
            {
                Log($"Seq element failed: {element} at {newPos}");
                return result;
            }

            if (gotPartial)
                isPartial = true;

            // Skip predicate nodes as they are not part of the AST
            if (node is not PredicateNode)
                elements.Add(node);

            newPos = parsedPos;
        }

        var seqCtx = isPartial ? new ParseContext(
                seq.Kind ?? "Seq",
                new SeqLocation(seq.Elements.Length),
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

    Terminal[] GetExpectedTerminals(Rule rule)
    {
        return rule switch
        {
            Terminal t => [t],
            Seq s when s.Elements.Length > 0 => GetExpectedTerminals(s.Elements[0]),
            Optional o => GetExpectedTerminals(o.Element),
            OftenMissed om => GetExpectedTerminals(om.Element),
            OneOrMany o => GetExpectedTerminals(o.Element),
            ZeroOrMany z => GetExpectedTerminals(z.Element),
            SeparatedList sl => GetExpectedTerminals(sl.Element),
            _ => []
        };
    }

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

        var maxFailPos = firstResult.MaxFailPos;
        var isPartial = gotPartial;

        elements.Add(firstNode);
        currentPos = newPos;

        // Последующие элементы
        while (true)
        {
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
                            var sepRequiredCtx = new ParseContext(listRule.Kind, new SeqLocation(elements.Count), GetExpectedTerminals(listRule.Separator), null);
                            return Result.Partial(new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos), currentPos, maxFailPos, sepRequiredCtx);
                        }
                        return Result.Failure(maxFailPos);
                    }

                break;
            }

            if (gotPartial)
                isPartial = true;

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
                        var sepForbiddenCtx1 = new ParseContext(listRule.Kind, new SeqLocation(elements.Count), [], null);
                        return Result.Partial(new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos), currentPos, maxFailPos, sepForbiddenCtx1);
                    }
                    return Result.Failure(maxFailPos);
                }

                break;
            }

            if (gotPartial)
                isPartial = true;

            elements.Add(elemNode);
            currentPos = newPos;
        }

        if (listRule.EndBehavior == SeparatorEndBehavior.Forbidden)
        {
            var sepResult = ParseAlternative(listRule.Separator, currentPos, input);
            if (sepResult.MaxFailPos > maxFailPos)
                maxFailPos = sepResult.MaxFailPos;
            if (sepResult.TryGetSuccess(out _, out _))
            {
                Log($"End sepearator should not be present {currentPos}.");
                if (isPartial)
                {
                    var sepForbiddenCtx2 = new ParseContext(listRule.Kind, new SeqLocation(elements.Count), [], null);
                    return Result.Partial(new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos), currentPos, maxFailPos, sepForbiddenCtx2);
                }
                return Result.Failure(maxFailPos);
            }
        }

        var listNode = new ListNode(listRule.Kind, elements, delimiters, startPos, currentPos);
        var listCtxFinal = isPartial ? new ParseContext(listRule.Kind, new SeqLocation(elements.Count), [], null) : null;
        if (isPartial)
            return Result.Partial(listNode, currentPos, maxFailPos, listCtxFinal!);
        return Result.Success(listNode, currentPos, maxFailPos);
    }
}
