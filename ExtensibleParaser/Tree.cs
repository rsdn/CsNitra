using System.Diagnostics;

namespace ExtensibleParaser;

[DebuggerTypeProxy(typeof(TreeDebugView))]
public sealed record Tree(string Input, object Element)
{
    public override string ToString() => $"«{((ISyntaxNode)Element).ToString(Input)}»  {Element}";
    public Tree[] Elements => Element switch
    {
        ReqRefNode x => [],
        RefNode x => [],
        ChoiceNode x => x.Alternatives.Select(x => new Tree(Input, x)).ToArray(),
        SeqNode x => x.Elements.Select(x => new Tree(Input, x)).ToArray(),
        TerminalNode x => [],
        _ => throw new ArgumentOutOfRangeException(Element.GetType().Name)
    };

    // Внутренний класс для отображения в отладчике
    private sealed class TreeDebugView(Tree Tree)
    {
        public Tree[] Elements => Tree.Elements;
    }
}