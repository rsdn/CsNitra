using System.Diagnostics;

namespace ExtensibleParaser;

[DebuggerTypeProxy(typeof(TreeDebugView))]
public sealed record Tree(string Input, object Element)
{
    public override string ToString() => $"«{((ISyntaxNode)Element).ToString(Input)}»  {Element}";
    public Tree[] Elements => (ISyntaxNode)Element switch
    {
        SeqNode x => x.Elements.Select(x => new Tree(Input, x)).ToArray(),
        SomeNode x => new Tree(Input, x.Value).Elements,
        TerminalNode x => [],
        _ => throw new ArgumentOutOfRangeException(Element.GetType().Name)
    };

    // Внутренний класс для отображения в отладчике
    private sealed class TreeDebugView(Tree Tree)
    {
        public Tree[] Elements => Tree.Elements;
    }
}