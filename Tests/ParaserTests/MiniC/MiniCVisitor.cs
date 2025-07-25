#nullable enable

using ExtensibleParaser;

using System.Diagnostics;

namespace MiniC;

public partial class MiniCTests
{
    private class MiniCVisitor(string input) : ISyntaxVisitor
    {
        private readonly List<Ast> _results = new();
        public Ast? Result { get; private set; }
        public string Input { get; } = input;

        public void Visit(TerminalNode node)
        {
            var result = node.Kind switch
            {
                "ErrorOperator" => new UnexpectedOperator(node),
                "Error" => new Error(node),
                "Number" => new Number(int.Parse(node.ToString(Input))),
                "Ident" => new Identifier(node.ToString(Input)),
                _ => new Token(node.AsSpan(Input).ToString())
            };
            _results.Add(result);
            Result = result;
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
                "FunctionDecl" => new FunctionDecl(
                    (Identifier)children[1]!,
                    children.Length > 4 && children[3] is Params p ? p.Parameters : new List<Identifier>(),
                    (Block)children[^1]!
                ),
                "ParamsList" => handleParamsList(children),
                "ModuleFunctions" => new Block(
                    children.Select(c => c!).ToList(),
                    HasBraces: false
                ),
                "CallNoArgs" => new CallExpr(
                    (Identifier)children[0]!,
                    new Args(new List<Expr>())
                ),
                "Call" => new CallExpr(
                    (Identifier)children[0]!,
                    new Args(getCallArguments(children))
                ),
                "Return" => new ReturnStmt((Expr)children[1]!),
                "ExprStmt" => new ExprStmt((Expr)children[0]!),
                "VarDecl" => new VarDecl((Token)children[0]!, (Identifier)children[1]!),
                "ArrayDecl" => new ArrayDecl(
                    (Token)children[0]!,
                    (Identifier)children[3]!,
                    children[6] is ArrayDeclItems p ? p.Numbers : new List<Number>()),
                "IfStmt" => new IfStatement(
                    (Expr)children[2]!,
                    wrapInBlockIfNeeded(children[4]!),
                    null
                ),
                "IfElseStmt" => new IfStatement(
                    (Expr)children[2]!,
                    wrapInBlockIfNeeded(children[4]!),
                    wrapInBlockIfNeeded(children[6]!)
                ),
                "MultiBlock" => new Block(
                    children
                        .Skip(1)
                        .Take(children.Length - 2)
                        .SelectMany(c => c is Block b ? b.Statements : new List<Ast> { c! })
                        .ToList(),
                    HasBraces: true),
                "SimplBlock" => wrapInBlockIfNeeded(children[0]!),
                "ZeroOrMany" => new Block(children.SelectMany(c =>
                    c is Block b ? b.Statements : new List<Ast> { c! }).ToList()),
                "Add" => makeBinaryOperator("+", children),
                "Sub" => makeBinaryOperator("-", children),
                "Mul" => makeBinaryOperator("*", children),
                "Div" => makeBinaryOperator("/", children),
                "Eq"  => makeBinaryOperator("==", children),
                "Neq" => makeBinaryOperator("!=", children),
                "Lt"  => makeBinaryOperator("<", children),
                "Gt"  => makeBinaryOperator(">", children),
                "Le"  => makeBinaryOperator("<=", children),
                "Ge"  => makeBinaryOperator(">=", children),
                "And" => makeBinaryOperator("&&", children),
                "Or"  => makeBinaryOperator("||", children),
                "RecoveryOperator" => makeRecoveryBinaryOperator(children),
                "RecoveryEmptyOperator" => makeMissingBinaryOperator(children),
                "Neg" => new UnaryExpr("-", (Expr)children[1]!),
                "AssignmentExpr" => new BinaryExpr("=", (Expr)children[0]!, (Expr)children[2]!),
                "ParamsRest" => children[1]!, // Just return the parameter part (skip the comma)
                "ArgsRest" => children[1]!, // Just return the parameter part (skip the comma)
                "ArrayDeclItems" => children[1]!, //Just return the parameter part (skip the comma)
                _ => throw new InvalidOperationException($"Unknown sequence: {node}: «{node.AsSpan(Input)}»")
            };

            Trace.WriteLine($"Created {Result?.GetType().Name}: {Result}");

            return;

            Expr makeRecoveryBinaryOperator(Ast?[] children)
            {
                Guard.IsTrue(children.Length == 3);
                return new BinaryExpr($"«Unexpected: {((UnexpectedOperator)children[1]!).ErrorNode.ToString(Input)}»", (Expr)children[0]!, (Expr)children[2]!);
            }

            Expr makeMissingBinaryOperator(Ast?[] children)
            {
                Guard.IsTrue(children.Length == 3);
                return new BinaryExpr($"«Missing operator»", (Expr)children[0]!, (Expr)children[2]!);
            }

            Expr makeBinaryOperator(string Op, Ast?[] children)
            {
                Guard.IsTrue(children.Length == 3);
                return new BinaryExpr(Op, (Expr)children[0]!, (Expr)children[2]!);
            }

            static Block wrapInBlockIfNeeded(Ast ast) => ast is Block block ? block : new Block(new List<Ast> { ast }, HasBraces: ast is not ExprStmt);
            static Params handleParamsList(Ast?[] children)
            {
                var parameters = new List<Identifier>();

                // Первый параметр
                if (children.Length > 0 && children[0] is Identifier firstParam)
                {
                    parameters.Add(firstParam);
                }

                // Остальные параметры (из ZeroOrMany)
                for (int i = 1; i < children.Length; i++)
                {
                    if (children[i] is Block block)
                    {
                        parameters.AddRange(block.Statements.OfType<Identifier>());
                    }
                    else if (children[i] is Identifier id)
                    {
                        parameters.Add(id);
                    }
                }

                return new Params(parameters);
            }
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
                "ParamsRest" => new Params(items.OfType<Identifier>().ToList()),
                "ArgsRest" => new Args(items.OfType<Expr>().ToList()),
                "ArrayDeclItems" => new ArrayDeclItems(items.OfType<Number>().ToList()),
                _ => throw new InvalidOperationException($"Unknown sequence: {node}: «{node.AsSpan(Input)}»")
            };

            Trace.WriteLine($"Processed list with {items.Length} elements");
        }

        public void Visit(SomeNode node) => node.Value.Accept(this);
        public void Visit(NoneNode node) => Result = null;
    public void Visit(SkippedNode node)
    {
        // For testing purposes, we represent skipped nodes as a simple token.
        // In a real scenario, this might involve more complex logic,
        // like attaching the skipped text as a diagnostic.
        _results.Add(new Token("«Skipped»"));
    }
    }
}
