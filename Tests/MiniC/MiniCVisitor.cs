#nullable enable

using ExtensibleParaser;

using System.Diagnostics;

namespace MiniC;

public partial class MiniCTests
{
    private class MiniCVisitor(string input) : ISyntaxVisitor
    {
        public Ast? Result { get; private set; }
        public string Input { get; } = input;

        public void Visit(TerminalNode node)
        {
            Result = node.Kind switch
            {
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
                "Add" => new BinaryExpr("+", (Expr)children[0]!, (Expr)children[2]!),
                "Sub" => new BinaryExpr("-", (Expr)children[0]!, (Expr)children[2]!),
                "Mul" => new BinaryExpr("*", (Expr)children[0]!, (Expr)children[2]!),
                "Div" => new BinaryExpr("/", (Expr)children[0]!, (Expr)children[2]!),
                "Eq" => new BinaryExpr("==", (Expr)children[0]!, (Expr)children[2]!),
                "Neq" => new BinaryExpr("!=", (Expr)children[0]!, (Expr)children[2]!),
                "Lt" => new BinaryExpr("<", (Expr)children[0]!, (Expr)children[2]!),
                "Gt" => new BinaryExpr(">", (Expr)children[0]!, (Expr)children[2]!),
                "Le" => new BinaryExpr("<=", (Expr)children[0]!, (Expr)children[2]!),
                "Ge" => new BinaryExpr(">=", (Expr)children[0]!, (Expr)children[2]!),
                "And" => new BinaryExpr("&&", (Expr)children[0]!, (Expr)children[2]!),
                "Or" => new BinaryExpr("||", (Expr)children[0]!, (Expr)children[2]!),
                "Neg" => new UnaryExpr("-", (Expr)children[1]!),
                "AssignmentExpr" => new BinaryExpr("=", (Expr)children[0]!, (Expr)children[2]!),
                "ArgsRest" => children[1]!, // Just return the expression part (skip the comma)
                "ParamsRest" => children[1]!, // Just return the parameter part (skip the comma)
                _ => throw new InvalidOperationException($"Unknown sequence: {node}: «{node.AsSpan(Input)}»")
            };

            Trace.WriteLine($"Created {Result?.GetType().Name}: {Result}");

            return;

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
            static List<Expr> getCallArguments(IReadOnlyList<Ast?> children)
            {
                var args = new List<Expr>();
                // First argument is after '('
                if (children.Count > 2 && children[2] is Expr firstArg)
                {
                    args.Add(firstArg);
                }

                // Additional arguments are in ZeroOrMany nodes
                for (int i = 3; i < children.Count - 1; i++)
                {
                    if (children[i] is Block block)
                    {
                        args.AddRange(block.Statements.OfType<Expr>());
                    }
                    else if (children[i] is Expr expr)
                    {
                        args.Add(expr);
                    }
                }
                return args;
            }
        }

        public void Visit(SomeNode node) => node.Value.Accept(this);
        public void Visit(NoneNode node) => Result = null;
        public void Visit(ChoiceNode node) => node.Alternatives[0].Accept(this);
        public void Visit(RefNode node) => node.Inner.Accept(this);
        public void Visit(ReqRefNode node) => node.Inner.Accept(this);
    }
}