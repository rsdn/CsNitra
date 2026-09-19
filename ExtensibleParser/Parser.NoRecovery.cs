#nullable enable

using Diagnostics;
using ExtensibleParser.Recovery;

namespace ExtensibleParser;

#if !RECOVERY

// Вариант без recovery (EnableRecovery=false): хуки тривиальны (константы/no-op),
// чтобы в Release JIT инлайнил и свернул их в ноль. Состояния recovery-подсистемы нет.
public partial class Parser
{
    private partial void OnMismatch(Terminal terminal, int pos) { }
    private partial void OnMemoWritten((int pos, string rule, int precedence) key) { }
    private partial void OnMemoRemoved((int pos, string rule, int precedence) key) { }
    private partial void OnInjectionApplied((int Pos, Terminal Terminal) key) { }
    private partial void OnPartialCaptured(Result partialResult) { }
    private partial bool IsRecoveryPosition(int pos) => false;
    private partial bool SuppressSideEffects => false;
    private partial void ResetRecoveryPoint() { }
    private partial bool InQuietZone(int pos) => false;
    public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics => [];
    private partial Result Recover(string input, string startRule, int currentStartPos) => ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
    private partial Result Speculative(Func<Result> parse)
    {
        var savedErrorPos = ErrorPos;
        var savedExpected = _expected.ToArray();
        try
        {
            return parse();
        }
        finally
        {
            ErrorPos = savedErrorPos;
            _expected = new HashSet<Terminal>(savedExpected);
        }
    }

    // Финализация без recovery: однократный парсинг, падение на MaxFailPos (ErrorPos/_expected).
    private partial void FinalizeResult(Result result, string input) =>
        ErrorInfo = result.IsSuccess ? null : new FatalError(input, ErrorPos, Location: input.PositionToLineCol(ErrorPos), _expected.ToArray());

    private partial void InitFollowCalculator(Dictionary<string, Rule[]> rules) { }
    private partial void SetMaxParseDepth(int inputLength) { }
    private partial void ClearInjections() { }
    private partial bool BeginParseFrame(int startPos) => false;
    private partial void EndParseFrame() { }
    private partial void PushRuleFrame(string ruleName, int minPrecedence, FrameLocation location, RecoveryOptions? options) { }
    private partial void PopFrame() { }
    private partial Result WithFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, RecoveryOptions? options, Func<Result> action) => action();

    private partial Terminal[] ExpectedFor(Rule element) => [];
    private partial RecoveryOptions? OptionsFor(Rule rule) => null;
    private partial RecoveryOptions? OftenMissedOptionsFor(Rule rule) => null;
    private partial Rule[] PrefixesFor(string ruleName, bool isRecoveryPos) => TdoppRules[ruleName].Prefix;
    private partial RuleWithPrecedence[] PostfixesFor(string ruleName, bool isRecoveryPos) => TdoppRules[ruleName].Postfix;
    private partial Result? InjectionAt(int pos, Terminal terminal) => null;
    private partial bool AcceptEpsilonMatch(Rule prefix) => false;
    // Recovery-альтернативы в no-recovery режиме не используются: они участвуют только в
    // recovery-позициях, которых здесь нет. Поэтому IsRecoveryRule исключает их из основного
    // Prefix (как в recovery-режиме), а ParseRecoveryRuleType их не разворачивает.
    private partial Result? ParseRecoveryRuleType(Rule rule, int startPos, string input) => null;
    private partial Result? InsertOftenMissed(OftenMissed oftenMissed, int startPos, Result failedResult) => null;
    private partial bool IsRecoveryTerminal(Terminal terminal) => false;
    private partial bool IsRecoveryRule(Rule alt) => alt.GetSubRules<RecoveryTerminal>().Any();
    private partial (Rule[] Prefix, RuleWithPrecedence[] Postfix) BuildRecoveryTdopp(string ruleName, Rule[] alternatives) => ([], []);
}

#endif
