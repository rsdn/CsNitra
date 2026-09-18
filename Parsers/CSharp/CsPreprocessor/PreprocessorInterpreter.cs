using System.Collections.Generic;
using System.Linq;
using ExtensibleParser;

namespace CsPreprocessor;

public sealed class PreprocessorInterpreter(string source, IEnumerable<string> commandLineSymbols) : ISyntaxVisitor
{
    private readonly char[] _text = source.ToCharArray();

    private readonly DirectiveStack _stack = new(commandLineSymbols.ToList());

    private readonly List<Diagnostic> _diagnostics = [];

    private readonly List<LineDirective> _lineDirectives = [];

    public PreprocessResult? Result { get; private set; }

    public void Visit(TerminalNode _)
    {
    }

    public void Visit(SeqNode node)
    {
        if (node.Kind is not "PreprocessorFile")
            return;

        foreach (var line in node.Elements)
            ProcessLine(line);

        Result = new PreprocessResult(new string(_text), _diagnostics, _lineDirectives);
    }

    public void Visit(ListNode _)
    {
    }

    public void Visit(SomeNode _)
    {
    }

    public void Visit(NoneNode _)
    {
    }

    private void ProcessLine(ISyntaxNode line)
    {
        switch (line)
        {
            case TerminalNode { Kind: "CodeLine" }:
                if (!_stack.IsActive)
                    Blank(line.StartPos, line.EndPos);
                break;

            case SeqNode { Kind: "DirectiveLine" } directiveLine:
                ProcessDirective(directiveLine);
                Blank(directiveLine.StartPos, directiveLine.EndPos);
                break;
        }
    }

    private void ProcessDirective(SeqNode directiveLine)
    {
        var directive = FindDirective(directiveLine);
        if (directive is null)
            return;

        switch (directive.Kind)
        {
            case "If":
                _stack.If(EvaluateCondition(directive));
                break;
            case "Elif":
                _stack.Elif(EvaluateCondition(directive));
                break;
            case "Else":
                _stack.Else();
                break;
            case "EndIf":
                _stack.EndIf();
                break;
            case "Define":
                _stack.Define(GetSymbolText(directive));
                break;
            case "Undef":
                _stack.Undef(GetSymbolText(directive));
                break;
            default:
                break;
        }
    }

    private static SeqNode? FindDirective(SeqNode directiveLine)
    {
        foreach (var element in directiveLine.Elements)
            if (element is SeqNode directive && IsDirectiveKind(directive.Kind))
                return directive;

        return null;
    }

    private static bool IsDirectiveKind(string kind)
        => kind is "If" or "Elif" or "Else" or "EndIf" or "Define" or "Undef"
            or "Error" or "Warning" or "LineDir" or "Region" or "EndRegion"
            or "Pragma" or "Nullable" or "Shebang" or "BadDirective";

    private string GetSymbolText(SeqNode directive)
        => directive.Elements.OfType<TerminalNode>().FirstOrDefault(t => t.Kind == "Symbol")?.ToString(source) ?? string.Empty;

    private bool EvaluateCondition(SeqNode directive)
    {
        var condition = directive.Elements
            .Single(e => e.Kind is not ("If" or "Elif" or "Ws" or "LineEnd"));
        return Evaluate(condition);
    }

    private bool Evaluate(ISyntaxNode node)
    {
        if (node is TerminalNode { Kind: "Symbol" } symbol)
        {
            var text = symbol.ToString(source);
            return text switch
            {
                "true" => true,
                "false" => false,
                _ => _stack.IsDefined(text)
            };
        }

        if (node is not SeqNode seq)
            throw new InvalidOperationException($"Unexpected condition node: {node.Kind}");

        var nonWs = seq.Elements.Where(e => e.Kind != "Ws").ToList();
        return seq.Kind switch
        {
            "Paren" => Evaluate(nonWs.Single(e => e.Kind is not ("OpenParen" or "CloseParen"))),
            "CondNot" => !Evaluate(nonWs[^1]),
            "CondAnd" => Evaluate(nonWs[0]) && Evaluate(nonWs[^1]),
            "CondOr" => Evaluate(nonWs[0]) || Evaluate(nonWs[^1]),
            "CondEq" => Evaluate(nonWs[0]) == Evaluate(nonWs[^1]),
            "CondNotEq" => Evaluate(nonWs[0]) != Evaluate(nonWs[^1]),
            _ => throw new InvalidOperationException($"Unexpected condition node Kind: {seq.Kind}")
        };
    }

    private void Blank(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var c = _text[i];
            if (c is '\n' or '\r')
                continue;
            _text[i] = ' ';
        }
    }
}
