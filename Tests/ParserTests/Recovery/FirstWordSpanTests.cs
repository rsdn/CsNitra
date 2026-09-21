#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-3 (7.2.1/R5): the Skipped diagnostic of an absorber region [E..S) spans the FIRST non-trivia
// "word" inside the region, not the whole region (IDE squiggle quality: a short label on the first
// "guilty" word). The absorber NODE in the tree keeps the full region span — only the diagnostic span
// shrinks. All-trivia region fallback: the original (full) span is kept.
[TestClass]
public sealed class FirstWordSpanTests
{
    // ============ 1. S6 trailing region with several words: the diagnostic is on the first word only ============
    // Module := Expr Expr, Expr := Number (the UnrecoveredTests shape). "12 34" is parsed (NewPos 6 —
    // includes trailing trivia); the trailing region [6..17) "### bar baz" is absorbed by the S6 floor.
    // The Skipped diagnostic must span the first word "###" [6..9) — NOT the whole region [6..17).
    [TestMethod]
    public void Test_S6_TrailingRegion_DiagnosticSpansFirstWordOnly()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["Expr"] = [RecoveryTerminals.Number()];
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.BuildTdoppRules();

        var input = "12 34 ### bar baz";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        var skipped = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.AreEqual(1, skipped.Count,
            $"expected exactly 1 Skipped (the S6 absorber), got: {Describe(parser.RecoveryDiagnostics)}");

        // The absorber NODE keeps the full region [6..17) "### bar baz".
        var absorber = FindAbsorber(node!);
        Assert.IsNotNull(absorber, $"no absorber node in the tree, diags: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual(6, absorber!.StartPos, $"absorber must keep the full region start, got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual(input.Length, absorber.EndPos, "absorber must keep the full region end (EOF)");

        // A5-3: the diagnostic spans the first word "###" [6..9) only — not the whole region.
        var wordStart = input.IndexOf("###");
        Assert.AreEqual(wordStart, skipped[0].StartPos,
            $"diagnostic must start at the first word, got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual(wordStart + 3, skipped[0].EndPos,
            $"diagnostic must end after the first word (not at the region end {input.Length}), got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual("###", input.Substring(skipped[0].StartPos, skipped[0].EndPos - skipped[0].StartPos));
        Assert.IsTrue(skipped[0].EndPos < input.Length, "diagnostic must not span the whole region");
    }

    // ============ 2. S3 panic region starting at e (no leading whitespace): first word starts at e ============
    // Module := '{' ZeroOrMany(Stmt) '}', Stmt := Ident ':' Number ';'. "1" is parsed, ';' mismatches at
    // the first '#' (e=7). A skip strategy (S2 resync to the anchor / S3 panic) skips to the terminator
    // Ident "b" (S=11 — First(Stmt) is a loop terminator): region [7..11) "### ". The diagnostic must
    // span the first word "###" [7..10) — not the whole region. The valid Stmt "b: 2 ;" after the
    // garbage makes the resync complete (one skip, no cascade).
    [TestMethod]
    public void Test_S3_Region_DiagnosticSpansFirstWordOnly()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["Stmt"] = [new Seq([RecoveryTerminals.Ident(), new Literal(":"), RecoveryTerminals.Number(), new Literal(";")], "Stmt")];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Stmt"), "Stmts"), new Literal("}")], "Module")];
        parser.BuildTdoppRules();

        var input = "{ a: 1 ### b: 2 ; }";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        var skipped = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.AreEqual(1, skipped.Count,
            $"expected exactly 1 Skipped (the S3 absorber), got: {Describe(parser.RecoveryDiagnostics)}");

        var e = input.IndexOf('#');
        var regionEnd = input.IndexOf('b', e);

        // The absorber NODE keeps the full region [e..regionEnd) "### ".
        var absorber = FindAbsorber(node!);
        Assert.IsNotNull(absorber, $"no absorber node in the tree, diags: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual(e, absorber!.StartPos, "absorber must keep the full region start (e)");
        Assert.AreEqual(regionEnd, absorber.EndPos, "absorber must keep the full region end (the terminator)");

        // A5-3: the diagnostic spans the first word "###" [e..e+3) only — not the whole region.
        Assert.AreEqual(e, skipped[0].StartPos,
            $"diagnostic must start at the first word (e), got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual(e + 3, skipped[0].EndPos,
            $"diagnostic must end after the first word (not at the region end {regionEnd}), got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual("###", input.Substring(skipped[0].StartPos, skipped[0].EndPos - skipped[0].StartPos));
        Assert.IsTrue(skipped[0].EndPos < regionEnd, "diagnostic must not span the whole region");
    }

    // ============ 3. All-whitespace region: the documented fallback keeps the full region span ============
    // Hand-built tree (the SideTableTests pattern): an absorber [3..6) over "abc   def" — the region
    // "   " is all whitespace (no word). DeriveDiagnostics must keep the ORIGINAL region span [3..6)
    // (it matches the absorber node exactly; a zero-width span would be misclassified as an insertion
    // by the session-end shape match) and carry the full region text in Message.
    [TestMethod]
    public void Test_AllWhitespaceRegion_FallsBackToFullSpan()
    {
        var input = "abc   def";
        var absorber = new TerminalNode("Skipped", 3, 6, 3, IsRecovery: true, IsAbsorber: true);
        var root = new SeqNode("Module", [absorber], 3, 6);

        var diags = DiagnosticDerivation.DeriveDiagnostics(root, input);

        var skipped = diags.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.AreEqual(1, skipped.Count, $"expected exactly 1 Skipped (the absorber), got: {Describe(diags)}");
        Assert.AreEqual(3, skipped[0].StartPos, $"all-whitespace region must keep the full region start, got: {Describe(diags)}");
        Assert.AreEqual(6, skipped[0].EndPos, $"all-whitespace region must keep the full region end, got: {Describe(diags)}");
        Assert.AreEqual(input.Substring(3, 3), skipped[0].Message, "the diagnostic must carry the region text");
    }

    // ============ Хелперы ============

    // The absorber node in the final tree (the single source of truth for the full region [E..S)).
    private static TerminalNode? FindAbsorber(ISyntaxNode node)
    {
        switch (node)
        {
            case TerminalNode { IsAbsorber: true } t:
                return t;
            case SeqNode s:
                foreach (var el in s.RawElements)
                    if (FindAbsorber(el) is { } f)
                        return f;
                return null;
            case ListNode l:
                foreach (var el in l.RawElements)
                    if (FindAbsorber(el) is { } f)
                        return f;
                foreach (var d in l.Delimiters)
                    if (FindAbsorber(d) is { } f)
                        return f;
                return null;
            case SomeNode so:
                return FindAbsorber(so.Value);
            default:
                return null;
        }
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) \"{d.Message}\""));
}
