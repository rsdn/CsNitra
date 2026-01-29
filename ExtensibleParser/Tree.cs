using System.Diagnostics;

namespace ExtensibleParaser;

[DebuggerTypeProxy(typeof(TreeDebugView))]
public sealed record Tree(string Input, object Element)
{
    public override string ToString()
    {
        var element = (ISyntaxNode)Element;
        return $"{element.Kind}: «{element.ToString(Input)}»  {Element}";
    }

    public string Kind => ((ISyntaxNode)Element).Kind;

    public object Elements => (ISyntaxNode)Element switch
    {
        SeqNode x => x.Elements.Select(x => new Tree(Input, x)).ToArray(),
        SomeNode x => new Tree(Input, x.Value).Elements,
        TerminalNode x => new Tree[0],
        ListNode x => ToTreeArray(x),
        _ => $"Unsupported node type: {Element.GetType().Name}"
    };

    private Tree[] ToTreeArray(ListNode listNode)
    {
        var result = new List<Tree>();
        for (int i = 0; i < listNode.Elements.Count; i++)
        {
            result.Add(new Tree(Input, listNode.Elements[i]));
            if (i < listNode.Delimiters.Count)
                result.Add(new Tree(Input, listNode.Delimiters[i]));
        }

        return result.ToArray();
    }

    // Внутренний класс для отображения в отладчике
    private sealed class TreeDebugView(Tree Tree)
    {
        public object Elements => Tree.Elements;
    }
}
