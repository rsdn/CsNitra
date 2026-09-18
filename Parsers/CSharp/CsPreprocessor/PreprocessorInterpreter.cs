using System.Collections.Generic;
using System.Linq;
using ExtensibleParser;

namespace CsPreprocessor;

public sealed class PreprocessorInterpreter(string source, IEnumerable<string> commandLineSymbols) : ISyntaxVisitor
{
    private const string CodeError = "CS1029";

    private const string CodeWarning = "CS1030";

    private const string CodeStrayElse = "CS1025";

    private const string CodeStrayElif = "CS1028";

    private const string CodeStrayEndIf = "CS1023";

    private const string CodeUnterminatedIf = "CS1024";

    private const string CodeMultipleElse = "CS1034";

    private const string CodeElifAfterElse = "CS1035";

    private readonly char[] _text = source.ToCharArray();

    private readonly DirectiveStack _stack = new(commandLineSymbols.ToList());

    private readonly List<Diagnostic> _diagnostics = [];

    private readonly List<LineDirective> _lineDirectives = [];

    private readonly Stack<IfPosition> _openIfs = new();

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

        if (_openIfs.Count > 0)
        {
            var openIf = _openIfs.Peek();
            AddDiagnostic(
                openIf.StartPos,
                openIf.EndPos,
                DiagnosticSeverity.Error,
                "Undefined '#if' directive",
                CodeUnterminatedIf);
        }

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
                _openIfs.Push(new IfPosition(directiveLine.StartPos, directiveLine.EndPos));
                _stack.If(EvaluateCondition(directive));
                break;
            case "Elif":
                if (!_stack.HasUnfinishedIf)
                    AddDiagnostic(
                        directiveLine,
                        DiagnosticSeverity.Error,
                        "Unexpected '#elif' directive",
                        CodeStrayElif);
                else if (_stack.CurrentFrameHasElse)
                    AddDiagnostic(
                        directiveLine,
                        DiagnosticSeverity.Error,
                        "'#elif' directive after '#else'",
                        CodeElifAfterElse);
                else
                    _stack.Elif(EvaluateCondition(directive));
                break;
            case "Else":
                if (!_stack.HasUnfinishedIf)
                    AddDiagnostic(
                        directiveLine,
                        DiagnosticSeverity.Error,
                        "Unexpected '#else' directive",
                        CodeStrayElse);
                else if (_stack.CurrentFrameHasElse)
                    AddDiagnostic(
                        directiveLine,
                        DiagnosticSeverity.Error,
                        "Multiple '#else' directives",
                        CodeMultipleElse);
                else
                    _stack.Else();
                break;
            case "EndIf":
                if (!_stack.HasUnfinishedIf)
                    AddDiagnostic(
                        directiveLine,
                        DiagnosticSeverity.Error,
                        "Unexpected '#endif' directive",
                        CodeStrayEndIf);
                else
                    _openIfs.Pop();
                _stack.EndIf();
                break;
            case "Error":
                if (_stack.IsActive)
                    AddDiagnostic(
                        directiveLine,
                        DiagnosticSeverity.Error,
                        GetMessageText(directive, "error"),
                        CodeError);
                break;
            case "Warning":
                if (_stack.IsActive)
                    AddDiagnostic(
                        directiveLine,
                        DiagnosticSeverity.Warning,
                        GetMessageText(directive, "warning"),
                        CodeWarning);
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

    private void AddDiagnostic(SeqNode line, DiagnosticSeverity severity, string message, string? code)
        => AddDiagnostic(line.StartPos, line.EndPos, severity, message, code);

    private void AddDiagnostic(int start, int end, DiagnosticSeverity severity, string message, string? code)
        => _diagnostics.Add(new Diagnostic(message, start, end, severity, code));

    private string GetMessageText(SeqNode directive, string defaultMessage)
    {
        var lineEnd = directive.Elements.OfType<TerminalNode>().FirstOrDefault(t => t.Kind == "LineEnd");
        var text = lineEnd?.ToString(source)?.Trim() ?? string.Empty;
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1];
        return text.Length == 0 ? defaultMessage : text;
    }

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

    private sealed record IfPosition(int StartPos, int EndPos);
}
