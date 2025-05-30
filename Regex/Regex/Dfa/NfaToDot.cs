using System.Text;

namespace Regex;

public class DfaToDot
{
    public static string GenerateDot(DfaState startState, string regexPattern)
    {
        var dot = new StringBuilder();
        var visited = new HashSet<int>();

        dot.Append($$"""
            digraph DFA {
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

    public static void GenerateSvg(DfaState startState, string regexPattern, string outputPath) =>
        Dot.Dot2Svg("DFA", GenerateDot(startState, regexPattern), outputPath);

    private static void VisitState(DfaState state, StringBuilder dot, HashSet<int> visited)
    {
        if (visited.Contains(state.Id)) return;
        visited.Add(state.Id);

        if (state.IsFinal)
            dot.AppendLine($"    q{state.Id} [peripheries=2];");

        foreach (var transition in state.Transitions)
        {
            var label = Dot.EscapeLabel(transition.Condition.ToString());
            dot.AppendLine($$"""    q{{state.Id}} -> q{{transition.Target.Id}} [label="{{label}}"];""");
            VisitState(transition.Target, dot, visited);
        }
    }
}
