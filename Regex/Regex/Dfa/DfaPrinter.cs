using System.Text;

namespace Regex;

public static class DfaPrinter
{
    public static string Print(DfaState start)
    {
        var visited = new HashSet<DfaState>();
        var sb = new StringBuilder();
        PrintState(start, visited, sb);
        return sb.ToString().Trim();
    }

    private static void PrintState(DfaState state, HashSet<DfaState> visited, StringBuilder sb)
    {
        if (!visited.Add(state))
            return;

        sb.AppendLine($"State {state.Id}{(state.IsFinal ? " (final)" : "")}:");
        foreach (var t in state.Transitions)
            sb.AppendLine($"  {t.Condition} -> State {t.Target.Id}");
        
        foreach (var t in state.Transitions)
            PrintState(t.Target, visited, sb);
    }
}