

using System.Diagnostics;
using System.Xml.Linq;

namespace ExtensibleParaser;

/// <summary>
/// Represents a node in the syntax tree produced by the parser.
/// All syntax tree nodes implement this interface to support visitor pattern traversal.
/// </summary>
public interface ISyntaxNode
{
    /// <summary>
    /// Gets the kind/type name of this syntax node, used for pattern matching and semantic analysis.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Gets the starting position of this node in the input text (0-based index).
    /// </summary>
    int StartPos { get; }

    /// <summary>
    /// Gets the ending position of this node in the input text (exclusive, 0-based index).
    /// </summary>
    int EndPos { get; }

    /// <summary>
    /// Gets a value indicating whether this node was created during error recovery.
    /// Recovery nodes represent reconstructed syntax that wasn't fully present in the input.
    /// </summary>
    bool IsRecovery { get; }

    /// <summary>
    /// Accepts a visitor to traverse this syntax tree node.
    /// </summary>
    /// <param name="visitor">The visitor to accept.</param>
    void Accept(ISyntaxVisitor visitor);

    /// <summary>
    /// Gets the span of text from the input that this node represents.
    /// </summary>
    /// <param name="input">The original input text.</param>
    /// <returns>A span containing the node's text.</returns>
    ChatRef AsSpan(string input);

    /// <summary>
    /// Converts this node to a string representation using the original input text.
    /// </summary>
    /// <param name="input">The original input text.</param>
    /// <returns>A string representation of the node's content.</returns>
    string ToString(string input);
}

/// <summary>
/// Visitor interface for traversing syntax tree nodes.
/// Implements the visitor pattern for different node types.
/// </summary>
public interface ISyntaxVisitor
{
    /// <summary>Visits a terminal node.</summary>
    /// <param name="node">The terminal node to visit.</param>
    void Visit(TerminalNode node);

    /// <summary>Visits a sequence node.</summary>
    /// <param name="node">The sequence node to visit.</param>
    void Visit(SeqNode node);

    /// <summary>Visits a list node.</summary>
    /// <param name="node">The list node to visit.</param>
    void Visit(ListNode node);

    /// <summary>Visits a "some" optional node (containing a value).</summary>
    /// <param name="node">The some node to visit.</param>
    void Visit(SomeNode node);

    /// <summary>Visits a "none" optional node (representing absence of value).</summary>
    /// <param name="node">The none node to visit.</param>
    void Visit(NoneNode node);
}

/// <summary>
/// Base class for all syntax tree nodes.
/// Provides common functionality for tracking source positions and visitor pattern support.
/// </summary>
/// <param name="Kind">The kind/type name of this node.</param>
/// <param name="StartPos">The starting position in the input text.</param>
/// <param name="EndPos">The ending position in the input text (exclusive).</param>
/// <param name="IsRecovery">Whether this node was created during error recovery.</param>

[DebuggerDisplay("{Kind}: [{StartPos}-{EndPos}) {Length} {Debug()} ({GetType().Name})")]
[DebuggerTypeProxy(typeof(DebugView))]
public abstract record Node(string Kind, int StartPos, int EndPos, bool IsRecovery = false) : ISyntaxNode
{
    /// <summary>Gets the length of the text span represented by this node.</summary>
    public int Length => EndPos - StartPos;

    /// <summary>Gets the span of text from the input that this node represents.</summary>
    /// <param name="input">The original input text.</param>
    /// <returns>A span containing the node's text.</returns>
    public virtual ChatRef AsSpan(string input) => input.AsSpan(StartPos, EndPos - StartPos);

    /// <summary>Converts this node to a string representation using the original input text.</summary>
    /// <param name="input">The original input text.</param>
    /// <returns>A string representation of the node's content.</returns>
    public virtual string ToString(string input) => input[StartPos..EndPos];

    /// <summary>Accepts a visitor to traverse this syntax tree node.</summary>
    /// <param name="visitor">The visitor to accept.</param>
    public abstract void Accept(ISyntaxVisitor visitor);

#pragma warning disable CS0618 // Type or member is obsolete
    public string? Debug()
    {
        var input = Parser.Input;

        if (input == null)
            return null;

        var start = Math.Max(StartPos - 10, 0);
        var end = Math.Min(EndPos + 10, input.Length);

        return $"«{input[start..StartPos]}»  «{input[StartPos..EndPos]}»  «{input[EndPos..end]}»";
    }

    public string? DebugContent()
    {
        var input = Parser.Input;

        if (input == null)
            return null;

        return input[StartPos..EndPos];
    }

    private sealed class DebugView(Node node)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object Elements => Parser.Input == null ? new Tree[0] : new Tree(Parser.Input, node).Elements;
    }
    #pragma warning restore CS0618 // Type or member is obsolete
}

/// <summary>
/// Represents a terminal (leaf) node in the syntax tree that corresponds to a token in the input.
/// Terminal nodes directly reference matched text from the input without children.
/// </summary>
/// <param name="Kind">The kind/type name of this terminal (e.g., "Number", "Identifier").</param>
/// <param name="StartPos">The starting position in the input text.</param>
/// <param name="EndPos">The ending position in the input text (exclusive).</param>
/// <param name="ContentLength">The length of the actual matched content (may differ from EndPos-StartPos due to trivia).</param>
/// <param name="IsRecovery">Whether this node was created during error recovery.</param>
public record TerminalNode(string Kind, int StartPos, int EndPos, int ContentLength, bool IsRecovery = false) : Node(Kind, StartPos, EndPos, IsRecovery)
{
    /// <summary>Gets the span of the actual content (without trailing trivia).</summary>
    /// <param name="input">The original input text.</param>
    /// <returns>A span containing only the matched content.</returns>
    public override ChatRef AsSpan(string input) => input.AsSpan(StartPos, ContentLength);

    /// <summary>Converts the terminal to a string using only its content.</summary>
    /// <param name="input">The original input text.</param>
    /// <returns>The matched content as a string.</returns>
    public override string ToString(string input) => input.Substring(StartPos, ContentLength);

    /// <summary>Accepts a visitor to traverse this node.</summary>
    /// <param name="visitor">The visitor to accept.</param>
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);

    /// <summary>Gets a debug representation of this terminal node.</summary>
    /// <returns>A formatted debug string.</returns>
    public override string ToString() => $"{Kind}Terminal([{StartPos},{StartPos + ContentLength}), «{DebugContent() ?? Kind}»)";
}


/// <summary>
/// Represents a sequence node containing a list of child nodes parsed in order.
/// Seq nodes represent grammar rules that are composed of multiple sequential elements.
/// </summary>
/// <param name="Kind">The kind/type name of this sequence.</param>
/// <param name="Elements">The child nodes in order.</param>
/// <param name="StartPos">The starting position in the input text.</param>
/// <param name="EndPos">The ending position in the input text (exclusive).</param>
public record SeqNode(string Kind, IReadOnlyList<ISyntaxNode> Elements, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos)
{
    /// <summary>Accepts a visitor to traverse this node.</summary>
    /// <param name="visitor">The visitor to accept.</param>
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a list node containing elements separated by delimiters.
/// Used for parsing constructs like comma-separated lists in grammar rules.
/// </summary>
/// <param name="Kind">The kind/type name of this list.</param>
/// <param name="Elements">The list elements.</param>
/// <param name="Delimiters">The separator nodes between elements.</param>
/// <param name="StartPos">The starting position in the input text.</param>
/// <param name="EndPos">The ending position in the input text (exclusive).</param>
/// <param name="HasTrailingSeparator">Whether the list ends with a trailing separator.</param>
/// <param name="IsRecovery">Whether this node was created during error recovery.</param>
public record ListNode(string Kind, IReadOnlyList<ISyntaxNode> Elements, IReadOnlyList<ISyntaxNode> Delimiters, int StartPos, int EndPos, bool HasTrailingSeparator = false, bool IsRecovery = false)
    : Node(Kind, StartPos, EndPos, IsRecovery)
{
    /// <summary>Accepts a visitor to traverse this node.</summary>
    /// <param name="visitor">The visitor to accept.</param>
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);

    /// <summary>Converts the list to a string representation.</summary>
    /// <param name="input">The original input text.</param>
    /// <returns>A formatted string representation of the list elements.</returns>
    public override string ToString(string input) =>
        $"[{string.Join(", ", Elements.Select(e => e.ToString(input)))}]";
}

/// <summary>
/// Base class for optional (zero-or-one) syntax tree nodes.
/// Optional nodes represent grammar rules that may or may not match.
/// </summary>
/// <param name="Kind">The kind/type name of this optional node.</param>
/// <param name="StartPos">The starting position in the input text.</param>
/// <param name="EndPos">The ending position in the input text (exclusive).</param>
public abstract record OptionalNode(string Kind, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos);

/// <summary>
/// Represents an optional node that contains a value (the "Some" case).
/// Indicates that an optional grammar rule successfully matched.
/// </summary>
/// <param name="Kind">The kind/type name of this optional node.</param>
/// <param name="Value">The matched value.</param>
/// <param name="StartPos">The starting position in the input text.</param>
/// <param name="EndPos">The ending position in the input text (exclusive).</param>
public record SomeNode(string Kind, ISyntaxNode Value, int StartPos, int EndPos)
    : OptionalNode(Kind, StartPos, EndPos)
{
    /// <summary>Converts this node to a string representation.</summary>
    /// <param name="input">The original input text.</param>
    /// <returns>A formatted string representation.</returns>
    public override string ToString(string input) => $"SomeNode({Value.ToString(input)})";

    /// <summary>Accepts a visitor to traverse this node.</summary>
    /// <param name="visitor">The visitor to accept.</param>
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an optional node that is empty (the "None" case).
/// Indicates that an optional grammar rule did not match.
/// </summary>
/// <param name="Kind">The kind/type name of this optional node.</param>
/// <param name="StartPos">The starting position in the input text.</param>
/// <param name="EndPos">The ending position in the input text (exclusive).</param>
public record NoneNode(string Kind, int StartPos, int EndPos)
    : OptionalNode(Kind, StartPos, EndPos)
{
    /// <summary>Converts this node to a string representation.</summary>
    /// <param name="input">The original input text.</param>
    /// <returns>Always returns "None()".</returns>
    public override string ToString(string input) => "None()";

    /// <summary>Accepts a visitor to traverse this node.</summary>
    /// <param name="visitor">The visitor to accept.</param>
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a predicate node (&amp; or !) in the syntax tree.
/// Predicates do not consume input and should not appear in the final AST.
/// They are used internally during parsing for lookahead checks.
/// </summary>
public record PredicateNode(string Kind, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos)
{
    public override void Accept(ISyntaxVisitor visitor) => throw new InvalidOperationException("PredicateNode should not be visited");
    public override string ToString(string input) => $"Predicate({Kind})";
}

