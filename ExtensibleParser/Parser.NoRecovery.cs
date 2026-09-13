#nullable enable

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
    private partial Result Recover(string input, string startRule, int currentStartPos) => ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
    private partial Result Speculative(Func<Result> parse) => parse();
}

#endif
