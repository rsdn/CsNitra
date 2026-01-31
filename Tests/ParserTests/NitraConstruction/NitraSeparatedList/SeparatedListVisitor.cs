#nullable enable

using ExtensibleParser;
using NitraConstruction.Common;
using System.Diagnostics;

namespace NitraConstruction;

public partial class SeparatedListTests
{
    private class SeparatedListVisitor(string input) : ISyntaxVisitor
    {
        public Ast? Result { get; private set; }
        public string Input { get; } = input;

        public void Visit(TerminalNode node)
        {
            Result = node.Kind switch
            {
                "ErrorOperator" => new UnexpectedOperator(node),
                "Error" => new Error(node),
                "Number" => new Number(int.Parse(node.ToString(Input))),
                "Ident" => new Identifier(node.ToString(Input)),
                _ => new Token(node.AsSpan(Input).ToString())
            };
            Trace.WriteLine($"TerminalNode: {node.Kind} -> {Result}");
        }

        public void Visit(SeqNode node)
        {
            Trace.WriteLine($"\nProcessing SeqNode: {node.Kind} [{node.StartPos}-{node.EndPos}]");

            var children = node.Elements
                .Select(e =>
                {
                    e.Accept(this);
                    return Result;
                })
                .Where(r => r != null)
                .ToArray();

            Trace.WriteLine($"Children count: {children.Length}");
            for (int i = 0; i < children.Length; i++)
            {
                Trace.WriteLine($"  Child {i}: {children[i]?.GetType().Name} = {children[i]}");
            }

            Result = node.Kind switch
            {
                "CallOptional" => new CallExpr(
                    (Identifier)children[0]!,
                    new Args(getCallArguments(children))
                ),
                "CallRequired" => new CallExpr(
                    (Identifier)children[0]!,
                    new Args(getCallArguments(children))
                ),
                "CallForbidden" => new CallExpr(
                    (Identifier)children[0]!,
                    new Args(getCallArguments(children))
                ),
                _ => throw new InvalidOperationException($"Unknown sequence: {node}: «{node.AsSpan(Input)}»")
            };

            Trace.WriteLine($"Created {Result?.GetType().Name}: {Result}");

            return;

            static List<Expr> getCallArguments(Ast?[] children)
            {
                Guard.AreEqual(4, children.Length);

                var args = new List<Expr>();
                if (children[2] is Args listArgs)
                {
                    args.AddRange(listArgs.Arguments);
                }

                return args;
            }
        }

        public void Visit(ListNode node)
        {
            var items = node.Elements
                .Select(e =>
                {
                    e.Accept(this);
                    return Result;
                })
                .Where(r => r != null)
                .ToArray();

            Result = node.Kind switch
            {
                "ArgsRestOptional" => new Args(items.OfType<Expr>().ToList()),
                "ArgsRestRequired" => new Args(items.OfType<Expr>().ToList()),
                "ArgsRestForbidden" => new Args(items.OfType<Expr>().ToList()),
                _ => throw new InvalidOperationException($"Unknown sequence: {node}: «{node.AsSpan(Input)}»")
            };

            Trace.WriteLine($"Processed list with {items.Length} elements");
        }

        public void Visit(SomeNode node) => node.Value.Accept(this);
        public void Visit(NoneNode node) => Result = null;
    }
}
