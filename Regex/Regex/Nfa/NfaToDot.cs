using System.Text;

namespace Regex;

public class NfaToDot
{
    public static void GenerateSvg(NfaState startState, string regexPattern, string outputPath)
    {
        var dotContent = GenerateDot(startState, regexPattern);
        var tempDotFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempDotFile, dotContent);
            Dot.GenerateSvg(tempDotFile, outputPath);
        }
        finally
        {
            if (File.Exists(tempDotFile))
                File.Delete(tempDotFile);
        }
    }

    public static string GenerateDot(NfaState startState, string regexPattern)
    {
        var dot = new StringBuilder();
        var visited = new HashSet<int>();

        // Основная структура графа с использованием raw-литерала
        dot.Append($$"""
            digraph NFA {
                rankdir=LR;
                node [shape=circle];
                label="{{Dot.EscapeLabel(regexPattern)}}";
                labelloc=top;
                labeljust=left;
                
                start [shape=point, style=invis];
                start -> q{{startState.Id}} [label="start"];
            
            """);

        VisitState(startState, dot, visited);

        dot.AppendLine();
        dot.Append("}");
        return dot.ToString();
    }

    private static void VisitState(NfaState state, StringBuilder dot, HashSet<int> visited)
    {
        if (visited.Contains(state.Id)) return;
        visited.Add(state.Id);

        // Финализируем состояние
        if (state.IsFinal)
            dot.AppendLine($"    q{state.Id} [peripheries=2];");

        // Добавляем переходы
        foreach (var transition in state.Transitions)
        {
            var label = Dot.EscapeLabel(transition.Condition?.ToString() ?? "ε");
            dot.AppendLine($$"""    q{{state.Id}} -> q{{transition.Target.Id}} [label="{{label}}"];""");
            VisitState(transition.Target, dot, visited);
        }
    }
}