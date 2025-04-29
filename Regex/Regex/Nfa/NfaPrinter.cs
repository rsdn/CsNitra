using System.Text;

namespace Regex;

public static class NfaPrinter
{
    public static string Print(NfaState start)
    {
        var visited = new HashSet<NfaState>();
        var sb = new StringBuilder();
        PrintState(start, visited, sb);
        return sb.ToString().Trim();
    }

    private static void PrintState(NfaState state, HashSet<NfaState> visited, StringBuilder sb)
    {
        if (!visited.Add(state))
            return;

        sb.AppendLine($"State {state.Id}{(state.IsFinal ? " (final)" : "")}:");
        
        foreach (var t in state.Transitions)
            sb.AppendLine($"  {(t.Condition?.ToString() ?? "ε")} -> State {t.Target.Id}");
        
        foreach (var t in state.Transitions)
            PrintState(t.Target, visited, sb);
    }
}