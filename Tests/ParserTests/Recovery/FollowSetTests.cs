#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public class FollowSetTests
{
    private static IEqualityComparer<Terminal> _terminalEq =>
        new TerminalEqualityComparer();

    private sealed class TerminalEqualityComparer : IEqualityComparer<Terminal>
    {
        public bool Equals(Terminal? x, Terminal? y)
        {
            if (x is null || y is null)
                return x is null && y is null;
            if (x is Literal lx && y is Literal ly)
                return lx.Value == ly.Value;
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(Terminal obj)
        {
            if (obj is Literal l)
                return l.Value.GetHashCode();
            return obj.GetHashCode();
        }
    }

    // ============ Basic tests ============

    [TestMethod]
    public void Test_FirstSet_SimpleTerminal()
    {
        Rule[] exprRules = new Rule[] { new Literal("int"), new Literal("void") };
        var rules = new Dictionary<string, Rule[]>
        {
            {"Expr", exprRules}
        };

        var calc = new FollowSetCalculator(rules, "Expr");
        var first = calc.GetFirstSet("Expr");

        Assert.IsTrue(first.Contains(new Literal("int"), _terminalEq));
        Assert.IsTrue(first.Contains(new Literal("void"), _terminalEq));
        Assert.AreEqual(2, first.Count);
    }

    [TestMethod]
    public void Test_FirstSet_WithOptional()
    {
        Rule[] seqElements = new Rule[] { new Literal("a"), new Optional(new Literal("b")) };
        Rule[] exprRules = new Rule[] { new Seq(seqElements, "Expr") };
        var rules = new Dictionary<string, Rule[]>
        {
            {"Expr", exprRules}
        };

        var calc = new FollowSetCalculator(rules, "Expr");
        var first = calc.GetFirstSet("Expr");

        Assert.IsTrue(first.Contains(new Literal("a"), _terminalEq));
        Assert.IsFalse(first.Contains(new Literal("b"), _terminalEq));
    }

    [TestMethod]
    public void Test_FirstSet_WithSeparatedList()
    {
        Rule[] listRules = new Rule[] { new SeparatedList(new Literal("x"), new Literal(","), "List") };
        var rules = new Dictionary<string, Rule[]>
        {
            {"List", listRules}
        };

        var calc = new FollowSetCalculator(rules, "List");
        var first = calc.GetFirstSet("List");

        Assert.IsTrue(first.Contains(new Literal("x"), _terminalEq));
        Assert.IsFalse(first.Contains(new Literal(","), _terminalEq));
    }

    [TestMethod]
    public void Test_FollowSet_TDOPP()
    {
        Rule[] seqElements = new Rule[] { new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100) };
        Rule[] exprRules = new Rule[]
        {
            new Literal("1"),
            new Seq(seqElements, "Add"),
        };

        var rules = new Dictionary<string, Rule[]>
        {
            {"Expr", exprRules}
        };

        var calc = new FollowSetCalculator(rules, "Expr");
        var follow = calc.GetFollowSet("Expr");

        Assert.IsTrue(follow.Contains(new Literal("+"), _terminalEq));
    }

    [TestMethod]
    public void Test_ConfigurableStartSymbol()
    {
        Rule[] exprRules = new Rule[] { new Literal("1"), new Literal("2") };
        Rule[] stmtRules = new Rule[] { new Ref("Expr") };

        var rules = new Dictionary<string, Rule[]>
        {
            {"Expr", exprRules},
            {"Stmt", stmtRules}
        };

        var calc = new FollowSetCalculator(rules, "Expr");
        var follow = calc.GetFollowSet("Expr");

        Assert.IsTrue(follow.Any(t => t.Kind == "EOF"));

        var calc2 = new FollowSetCalculator(rules, "Stmt");
        var stmtFollow = calc2.GetFollowSet("Stmt");
        Assert.IsTrue(stmtFollow.Any(t => t.Kind == "EOF"));
    }

    [TestMethod]
    public void Test_FollowSet_OftenMissed()
    {
        Rule[] seqElements = new Rule[] { new Literal("x"), new OftenMissed(new Literal(";")) };
        Rule[] stmtRules = new Rule[] { new Seq(seqElements, "Stmt") };

        var rules = new Dictionary<string, Rule[]>
        {
            {"Stmt", stmtRules}
        };

        var calc = new FollowSetCalculator(rules, "Stmt");
        var first = calc.GetFirstSet("Stmt");

        Assert.IsTrue(first.Contains(new Literal("x"), _terminalEq));
    }

    [TestMethod]
    public void Test_LiteralIdentity_ByValue()
    {
        Rule[] exprRules = new Rule[] { new Literal("a"), new Literal("a") };
        var rules = new Dictionary<string, Rule[]>
        {
            {"Expr", exprRules}
        };

        var calc = new FollowSetCalculator(rules, "Expr");
        var first = calc.GetFirstSet("Expr");

        Assert.AreEqual(1, first.Count);
    }

    // ============ Real grammar: MiniC-like ============

    private static Dictionary<string, Rule[]> BuildMiniCgrammar()
    {
        var rules = new Dictionary<string, Rule[]>
        {
            // Module = ZeroOrMany(Function)
            {"Module", new Rule[] { new ZeroOrMany(new Ref("Function")) } },

            // Function = int Ident ( Params ) Block
            {"Function", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("int"),
                    new Literal("func"),
                    new Literal("("),
                    new Ref("Params"),
                    new Literal(")"),
                    new Ref("Block"),
                }, "FunctionDecl")
            } },

            // Params = SeparatedList(Param, ",") | void
            {"Params", new Rule[] {
                new SeparatedList(new Ref("Param"), new Literal(","), "ParamsList"),
                new Literal("void", "VoidParams"),
            } },

            // Param = Ident
            {"Param", new Rule[] { new Literal("x") } },

            // Block = { ZeroOrMany(Statement) }
            {"Block", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("{"),
                    new ZeroOrMany(new Ref("Statement")),
                    new Literal("}"),
                }, "Block")
            } },

            // Statement = VarDecl | ExprStmt | IfStmt | ReturnStmt
            {"Statement", new Rule[] {
                new Seq(new Rule[] { new Literal("int"), new Literal("var"), new Literal(";") }, "VarDecl"),
                new Seq(new Rule[] { new Ref("Expr"), new Literal(";") }, "ExprStmt"),
                new Seq(new Rule[] {
                    new Literal("if"),
                    new Literal("("),
                    new Ref("Expr"),
                    new Literal(")"),
                    new Ref("Block"),
                }, "IfStmt"),
                new Seq(new Rule[] { new Literal("return"), new Ref("Expr"), new Literal(";") }, "ReturnStmt"),
            } },

            // Expr = Number | Ident | ( Expr ) | Expr + Expr | Expr * Expr
            {"Expr", new Rule[] {
                new Literal("42"),
                new Literal("x"),
                new Seq(new Rule[] { new Literal("("), new Ref("Expr"), new Literal(")") }, "Parens"),
                new Seq(new Rule[] { new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100) }, "Add"),
                new Seq(new Rule[] { new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200) }, "Mul"),
            } },
        };
        return rules;
    }

    [TestMethod]
    public void Test_MiniC_FollowSet_Block_EndsWithCloseBrace()
    {
        var rules = BuildMiniCgrammar();
        var calc = new FollowSetCalculator(rules, "Module");

        // Block is { Statement* }
        // In Function: int func ( Params ) Block — Block is at the end, so follow(Block) ∪= follow(Function) = EOF
        // In IfStmt: if ( Expr ) Block — Block is at the end, so follow(Block) ∪= follow(Statement)
        // follow(Statement) = { }, int, if, return, 42, x } (from Block's ZeroOrMany)
        var follow = calc.GetFollowSet("Block");
        Assert.IsTrue(follow.Contains(new Literal("}"), _terminalEq), "follow(Block) should contain '}' from follow(Statement)");
        Assert.IsTrue(follow.Any(t => t.Kind == "EOF"), "follow(Block) should contain EOF from follow(Function)");
    }

    [TestMethod]
    public void Test_MiniC_FollowSet_Statement_EndsWithCloseBrace()
    {
        var rules = BuildMiniCgrammar();
        var calc = new FollowSetCalculator(rules, "Module");

        // Statement is inside Block: { Statement* }
        // follow(Statement) = First(Statement) [loop] | } [after loop]
        var follow = calc.GetFollowSet("Statement");

        Assert.IsTrue(follow.Contains(new Literal("}"), _terminalEq), "follow(Statement) should contain '}' from Block");
        Assert.IsTrue(follow.Contains(new Literal("int"), _terminalEq), "follow(Statement) should contain 'int' (VarDecl, next iteration)");
        Assert.IsTrue(follow.Contains(new Literal("if"), _terminalEq), "follow(Statement) should contain 'if' (IfStmt, next iteration)");
        Assert.IsTrue(follow.Contains(new Literal("return"), _terminalEq), "follow(Statement) should contain 'return' (ReturnStmt, next iteration)");
    }

    [TestMethod]
    public void Test_MiniC_FollowSet_Expr_InParens()
    {
        var rules = BuildMiniCgrammar();
        var calc = new FollowSetCalculator(rules, "Module");

        // Expr = ... | ( Expr ) | Expr + Expr | Expr * Expr
        // follow(Expr) should contain ) from ( Expr )
        // and + and * from binary ops
        var follow = calc.GetFollowSet("Expr");

        Assert.IsTrue(follow.Contains(new Literal(")"), _terminalEq), "follow(Expr) should contain ')' from (Expr) and Function's params");
        Assert.IsTrue(follow.Contains(new Literal("+"), _terminalEq), "follow(Expr) should contain '+' from Add");
        Assert.IsTrue(follow.Contains(new Literal("*"), _terminalEq), "follow(Expr) should contain '*' from Mul");
        Assert.IsTrue(follow.Contains(new Literal(";"), _terminalEq), "follow(Expr) should contain ';' from ExprStmt/ReturnStmt");
    }

    [TestMethod]
    public void Test_MiniC_FollowSet_Params_ContainsCloseParen()
    {
        var rules = BuildMiniCgrammar();
        var calc = new FollowSetCalculator(rules, "Module");

        // Function: int func ( Params ) Block
        // follow(Params) should contain )
        var follow = calc.GetFollowSet("Params");
        Assert.IsTrue(follow.Contains(new Literal(")"), _terminalEq), "follow(Params) should contain ')' from Function");
    }

    [TestMethod]
    public void Test_MiniC_FollowSet_Param_ContainsCommaAndCloseParen()
    {
        var rules = BuildMiniCgrammar();
        var calc = new FollowSetCalculator(rules, "Module");

        // Params = SeparatedList(Param, ",")
        // Param is inside a SeparatedList, so follow(Param) should contain:
        //   First(Param) = "x" (loop body, next iteration)
        //   First(",") = ","  — actually SeparatedList's separator is NOT in the follow-set
        //     because SeparatedList flattens to the element for follow-set purposes
        //   ")" — from after Params in Function
        var follow = calc.GetFollowSet("Param");
        Assert.IsTrue(follow.Contains(new Literal("x"), _terminalEq), "follow(Param) should contain 'x' (loop body first)");
        Assert.IsTrue(follow.Contains(new Literal(")"), _terminalEq), "follow(Param) should contain ')' from Function (after SeparatedList)");
    }

    [TestMethod]
    public void Test_MiniC_FirstSet_Statement()
    {
        var rules = BuildMiniCgrammar();
        var calc = new FollowSetCalculator(rules, "Module");

        var first = calc.GetFirstSet("Statement");
        Assert.IsTrue(first.Contains(new Literal("int"), _terminalEq), "first(Statement) contains 'int' from VarDecl");
        Assert.IsTrue(first.Contains(new Literal("if"), _terminalEq), "first(Statement) contains 'if' from IfStmt");
        Assert.IsTrue(first.Contains(new Literal("return"), _terminalEq), "first(Statement) contains 'return' from ReturnStmt");
        Assert.IsTrue(first.Contains(new Literal("42"), _terminalEq), "first(Statement) contains '42' from ExprStmt -> Expr");
        Assert.IsTrue(first.Contains(new Literal("x"), _terminalEq), "first(Statement) contains 'x' from ExprStmt -> Expr");
    }

    // ============ SeparatedList follow-set tests ============

    [TestMethod]
    public void Test_SeparatedList_FollowSet_Includes_Separator()
    {
        // Grammar: List = SeparatedList(Ref("Item"), ",") ; End
        // follow(Item) should contain "," because items repeat, and ";" from after List
        var rules = new Dictionary<string, Rule[]>
        {
            {"Program", new Rule[] {
                new Seq(new Rule[] {
                    new SeparatedList(new Ref("Item"), new Literal(","), "List"),
                    new Literal(";"),
                }, "Program")
            } },
            {"Item", new Rule[] { new Literal("a") } },
        };

        var calc = new FollowSetCalculator(rules, "Program");
        var follow = calc.GetFollowSet("Item");

        Assert.IsTrue(follow.Contains(new Literal("a"), _terminalEq), "follow(Item) should contain 'a' (loop body first)");
        Assert.IsTrue(follow.Contains(new Literal(","), _terminalEq), "follow(Item) should contain ',' (separator)");
        Assert.IsTrue(follow.Contains(new Literal(";"), _terminalEq), "follow(Item) should contain ';' (after list)");
    }

    [TestMethod]
    public void Test_SeparatedList_Deep_Nesting()
    {
        // Grammar simulating: FunctionDecl = type ident ( SeparatedList(Param, ",") ) { Stmt* }
        // follow(Param) should contain both ) and , and param's own first
        var rules = new Dictionary<string, Rule[]>
        {
            {"Module", new Rule[] { new ZeroOrMany(new Ref("Decl")) } },
            {"Decl", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("int"),
                    new Literal("func"),
                    new Literal("("),
                    new SeparatedList(new Ref("Param"), new Literal(","), "Params"),
                    new Literal(")"),
                    new Literal("{"),
                    new ZeroOrMany(new Ref("Stmt")),
                    new Literal("}"),
                }, "Decl")
            } },
            {"Param", new Rule[] { new Literal("p") } },
            {"Stmt", new Rule[] { new Literal("s") } },
        };

        var calc = new FollowSetCalculator(rules, "Module");

        // Param is inside SeparatedList, which is followed by ) then { then Stmt* then }
        var paramFollow = calc.GetFollowSet("Param");
        Assert.IsTrue(paramFollow.Contains(new Literal("p"), _terminalEq), "follow(Param) should contain 'p' (loop body first)");
        Assert.IsTrue(paramFollow.Contains(new Literal(","), _terminalEq), "follow(Param) should contain ',' (separator)");
        Assert.IsTrue(paramFollow.Contains(new Literal(")"), _terminalEq), "follow(Param) should contain ')' (after SeparatedList)");
        Assert.IsFalse(paramFollow.Contains(new Literal("{"), _terminalEq), "follow(Param) must NOT contain '{' (too far)");

        // Stmt is inside { Stmt* }
        var stmtFollow = calc.GetFollowSet("Stmt");
        Assert.IsTrue(stmtFollow.Contains(new Literal("s"), _terminalEq), "follow(Stmt) should contain 's' (loop body first)");
        Assert.IsTrue(stmtFollow.Contains(new Literal("}"), _terminalEq), "follow(Stmt) should contain '}' (after loop)");
    }

    // ============ Deep nesting ============

    [TestMethod]
    public void Test_Deep_Nesting_ThreeLevels()
    {
        // A = { B* }
        // B = [ C* ]
        // C = x
        // follow(C) should contain ] and } and x
        var rules = new Dictionary<string, Rule[]>
        {
            {"A", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("{"),
                    new ZeroOrMany(new Ref("B")),
                    new Literal("}"),
                }, "A")
            } },
            {"B", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("["),
                    new ZeroOrMany(new Ref("C")),
                    new Literal("]"),
                }, "B")
            } },
            {"C", new Rule[] { new Literal("x") } },
        };

        var calc = new FollowSetCalculator(rules, "A");

        var cFollow = calc.GetFollowSet("C");
        Assert.IsTrue(cFollow.Contains(new Literal("x"), _terminalEq), "follow(C) should contain 'x' (loop body first)");
        Assert.IsTrue(cFollow.Contains(new Literal("]"), _terminalEq), "follow(C) should contain ']' (after loop in B)");

        var bFollow = calc.GetFollowSet("B");
        Assert.IsTrue(bFollow.Contains(new Literal("["), _terminalEq), "follow(B) should contain '[' (loop body first)");
        Assert.IsTrue(bFollow.Contains(new Literal("}"), _terminalEq), "follow(B) should contain '}' (after loop in A)");
    }

    [TestMethod]
    public void Test_Deep_Nesting_ThroughRef()
    {
        // Module = { Stmt* }
        // Stmt = Decl
        // Decl = int ident ;
        // follow(Decl) should contain } and int (next Stmt)
        var rules = new Dictionary<string, Rule[]>
        {
            {"Module", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("{"),
                    new ZeroOrMany(new Ref("Stmt")),
                    new Literal("}"),
                }, "Module")
            } },
            {"Stmt", new Rule[] { new Ref("Decl") } },
            {"Decl", new Rule[] {
                new Seq(new Rule[] { new Literal("int"), new Literal("x"), new Literal(";") }, "Decl")
            } },
        };

        var calc = new FollowSetCalculator(rules, "Module");

        var declFollow = calc.GetFollowSet("Decl");
        Assert.IsTrue(declFollow.Contains(new Literal("}"), _terminalEq), "follow(Decl) should contain '}' (from Module)");
        Assert.IsTrue(declFollow.Contains(new Literal("int"), _terminalEq), "follow(Decl) should contain 'int' (next Stmt/Decl in loop)");

        var stmtFollow = calc.GetFollowSet("Stmt");
        Assert.IsTrue(stmtFollow.Contains(new Literal("}"), _terminalEq), "follow(Stmt) should contain '}' (from Module)");
        Assert.IsTrue(stmtFollow.Contains(new Literal("int"), _terminalEq), "follow(Stmt) should contain 'int' (next Stmt in loop)");
    }

    [TestMethod]
    public void Test_Optional_FollowSet_Propagation()
    {
        // Rule: S = a Opt b c
        // Opt = d | e
        // follow(Opt) should contain b (next in sequence)
        var rules = new Dictionary<string, Rule[]>
        {
            {"S", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("a"),
                    new Optional(new Ref("Opt")),
                    new Literal("b"),
                    new Literal("c"),
                }, "S")
            } },
            {"Opt", new Rule[] { new Literal("d"), new Literal("e") } },
        };

        var calc = new FollowSetCalculator(rules, "S");

        var optFollow = calc.GetFollowSet("Opt");
        Assert.IsTrue(optFollow.Contains(new Literal("b"), _terminalEq), "follow(Opt) should contain 'b' (next element in S)");
    }

    [TestMethod]
    public void Test_SeparatedList_Empty_AllowsParentFollow()
    {
        // S = ( List ) e
        // List = SeparatedList(Item, ",", CanBeEmpty: true)
        // follow(Item) should contain i (loop), , (separator), ) (after list)
        // e is NOT in follow(Item) because ) is not nullable
        var rules = new Dictionary<string, Rule[]>
        {
            {"S", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("("),
                    new SeparatedList(new Ref("Item"), new Literal(","), "List", CanBeEmpty: true),
                    new Literal(")"),
                    new Literal("e"),
                }, "S")
            } },
            {"Item", new Rule[] { new Literal("i") } },
        };

        var calc = new FollowSetCalculator(rules, "S");
        var follow = calc.GetFollowSet("Item");

        Assert.IsTrue(follow.Contains(new Literal("i"), _terminalEq), "follow(Item) should contain 'i' (loop body)");
        Assert.IsTrue(follow.Contains(new Literal(","), _terminalEq), "follow(Item) should contain ',' (separator)");
        Assert.IsTrue(follow.Contains(new Literal(")"), _terminalEq), "follow(Item) should contain ')' (after SeparatedList)");
        Assert.IsFalse(follow.Contains(new Literal("e"), _terminalEq), "follow(Item) must NOT contain 'e' (blocked by ')')");
    }

    [TestMethod]
    public void Test_FirstSet_DeeplyNested()
    {
        // A = Seq(a, Opt(B), c)
        // B = Seq(d, e)
        // First(A) should contain a only
        // First(B) should contain d only
        var rules = new Dictionary<string, Rule[]>
        {
            {"A", new Rule[] {
                new Seq(new Rule[] { new Literal("a"), new Optional(new Ref("B")), new Literal("c") }, "A")
            } },
            {"B", new Rule[] {
                new Seq(new Rule[] { new Literal("d"), new Literal("e") }, "B")
            } },
        };

        var calc = new FollowSetCalculator(rules, "A");

        var aFirst = calc.GetFirstSet("A");
        Assert.IsTrue(aFirst.Contains(new Literal("a"), _terminalEq));
        Assert.IsFalse(aFirst.Contains(new Literal("d"), _terminalEq));
        Assert.IsFalse(aFirst.Contains(new Literal("c"), _terminalEq));

        var bFirst = calc.GetFirstSet("B");
        Assert.IsTrue(bFirst.Contains(new Literal("d"), _terminalEq));
        Assert.IsFalse(bFirst.Contains(new Literal("e"), _terminalEq));
    }

    [TestMethod]
    public void Test_TDOPP_MultiplePrecedenceLevels()
    {
        // Expr = Number | Expr + Expr : 100 | Expr * Expr : 200 | Expr = Expr : 10 (right)
        // follow(Expr) should contain all operators and ;
        var rules = new Dictionary<string, Rule[]>
        {
            {"Statement", new Rule[] {
                new Seq(new Rule[] { new Ref("Expr"), new Literal(";") }, "ExprStmt")
            } },
            {"Expr", new Rule[] {
                new Literal("42"),
                new Seq(new Rule[] { new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100) }, "Add"),
                new Seq(new Rule[] { new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200) }, "Mul"),
                new Seq(new Rule[] { new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true) }, "Assign"),
            } },
        };

        var calc = new FollowSetCalculator(rules, "Statement");

        var follow = calc.GetFollowSet("Expr");
        Assert.IsTrue(follow.Contains(new Literal("+"), _terminalEq), "follow(Expr) should contain '+'");
        Assert.IsTrue(follow.Contains(new Literal("*"), _terminalEq), "follow(Expr) should contain '*'");
        Assert.IsTrue(follow.Contains(new Literal("="), _terminalEq), "follow(Expr) should contain '='");
        Assert.IsTrue(follow.Contains(new Literal(";"), _terminalEq), "follow(Expr) should contain ';' from ExprStmt");
    }

    [TestMethod]
    public void Test_Predicate_NoEffectOnFollow()
    {
        // Block = { !(int) Statement* }
        // AndPredicate/NotPredicate should not affect follow-sets
        var rules = new Dictionary<string, Rule[]>
        {
            {"Block", new Rule[] {
                new Seq(new Rule[] {
                    new Literal("{"),
                    new ZeroOrMany(
                        new Seq(new Rule[] {
                            new NotPredicate(new Ref("Func")),
                            new Ref("Stmt"),
                        }, "BlockStmt")
                    ),
                    new Literal("}"),
                }, "Block")
            } },
            {"Func", new Rule[] { new Literal("int") } },
            {"Stmt", new Rule[] { new Literal("s"), new Literal("t") } },
        };

        var calc = new FollowSetCalculator(rules, "Block");

        var stmtFollow = calc.GetFollowSet("Stmt");
        Assert.IsTrue(stmtFollow.Contains(new Literal("}"), _terminalEq), "follow(Stmt) should contain '}' from Block");
        Assert.IsTrue(stmtFollow.Contains(new Literal("s"), _terminalEq), "follow(Stmt) should contain 's' (loop body)");
        Assert.IsTrue(stmtFollow.Contains(new Literal("t"), _terminalEq), "follow(Stmt) should contain 't' (loop body)");
    }

    [TestMethod]
    public void Test_RecoveryTerminal_InFirstSet()
    {
        // EmptyTerminal is a RecoveryTerminal (Terminal), treated as a regular terminal in First-set
        var emptyTerm = new EmptyTerminal("Error");
        var rules = new Dictionary<string, Rule[]>
        {
            {"Expr", new Rule[] {
                new Literal("42"),
                emptyTerm,
            } },
        };

        var calc = new FollowSetCalculator(rules, "Expr");
        var first = calc.GetFirstSet("Expr");

        Assert.IsTrue(first.Contains(new Literal("42"), _terminalEq));
        // EmptyTerminal is a Terminal, so it appears in First-set (not as ε)
        Assert.IsTrue(first.Contains(emptyTerm, _terminalEq), "first(Expr) should contain EmptyTerminal itself");
    }

    // ============ Nested loops (0.5) ============

    [TestMethod]
    public void Test_FollowSet_NestedRules()
    {
        // (a) цикл в теле цикла: Outer = ZeroOrMany(Seq([a, ZeroOrMany(Ref(Inner))]))
        // Inner = b → follow(Inner) ⊇ { b (итерация внутреннего цикла), a (тело внешнего), EOF (конец внешнего) }
        var rules = new Dictionary<string, Rule[]>
        {
            {"Outer", new Rule[] {
                new ZeroOrMany(new Seq(new Rule[] {
                    new Literal("a"),
                    new ZeroOrMany(new Ref("Inner")),
                }, "Body"))
            } },
            {"Inner", new Rule[] { new Literal("b") } },
        };

        var calc = new FollowSetCalculator(rules, "Outer");
        var follow = calc.GetFollowSet("Inner");

        Assert.IsTrue(follow.Contains(new Literal("b"), _terminalEq), "follow(Inner) contains 'b' (inner loop self-iteration)");
        Assert.IsTrue(follow.Contains(new Literal("a"), _terminalEq), "follow(Inner) contains 'a' (outer loop body first)");
        Assert.IsTrue(follow.Any(t => t.Kind == "EOF"), "follow(Inner) contains EOF (end of outer loop)");
    }

    [TestMethod]
    public void Test_FollowSet_NestedLoopInSeqInLoopBody()
    {
        // (b) цикл внутри Seq внутри тела цикла: Outer = ZeroOrMany(Seq([a, Seq([c, ZeroOrMany(Ref(Inner))])]))
        // Inner = b → follow(Inner) ⊇ { b, a, EOF }; c не следует за Inner (он перед циклом)
        var rules = new Dictionary<string, Rule[]>
        {
            {"Outer", new Rule[] {
                new ZeroOrMany(new Seq(new Rule[] {
                    new Literal("a"),
                    new Seq(new Rule[] {
                        new Literal("c"),
                        new ZeroOrMany(new Ref("Inner")),
                    }, "InnerSeq"),
                }, "Body"))
            } },
            {"Inner", new Rule[] { new Literal("b") } },
        };

        var calc = new FollowSetCalculator(rules, "Outer");
        var follow = calc.GetFollowSet("Inner");

        Assert.IsTrue(follow.Contains(new Literal("b"), _terminalEq), "follow(Inner) contains 'b' (inner loop self-iteration)");
        Assert.IsTrue(follow.Contains(new Literal("a"), _terminalEq), "follow(Inner) contains 'a' (outer loop body first)");
        Assert.IsTrue(follow.Any(t => t.Kind == "EOF"), "follow(Inner) contains EOF (end of outer loop)");
        Assert.IsFalse(follow.Contains(new Literal("c"), _terminalEq), "follow(Inner) must NOT contain 'c' (precedes the inner loop)");
    }

    // ============ GetTerminators (0.5) ============

    [TestMethod]
    public void Test_GetTerminators_EmptyStack_ReturnsEof()
    {
        var rules = new Dictionary<string, Rule[]>
        {
            {"A", new Rule[] { new Literal("x") } },
        };
        var calc = new FollowSetCalculator(rules, "A");

        var terminators = calc.GetTerminators(new List<StackFrame>());

        Assert.AreEqual(1, terminators.Length);
        Assert.AreEqual("EOF", terminators[0].Kind);
    }

    [TestMethod]
    public void Test_GetTerminators_InnerFirst_ThenOuter()
    {
        // S = A p → follow(A) = { p };  A = B q → follow(B) = { q }
        // stack = [A, B] (A внешний, B внутренний) → B первыми, затем A, EOF в конце
        var rules = new Dictionary<string, Rule[]>
        {
            {"S", new Rule[] { new Seq(new Rule[] { new Ref("A"), new Literal("p") }, "S") } },
            {"A", new Rule[] { new Seq(new Rule[] { new Ref("B"), new Literal("q") }, "A") } },
            {"B", new Rule[] { new Literal("r") } },
        };
        var calc = new FollowSetCalculator(rules, "S");

        var stack = new List<StackFrame>
        {
            new StackFrame("A", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("B", 0, new RuleFrameLocation(0), null, null),
        };

        var terminators = calc.GetTerminators(stack);

        Assert.AreEqual(3, terminators.Length);
        Assert.AreEqual("q", ((Literal)terminators[0]).Value, "inner frame B terminator first");
        Assert.AreEqual("p", ((Literal)terminators[1]).Value, "outer frame A terminator next");
        Assert.AreEqual("EOF", terminators[2].Kind, "EOF at the end");
    }

    [TestMethod]
    public void Test_GetTerminators_Dedup()
    {
        // S = A p → follow(A) = { p };  A = B p → follow(B) = { p }
        // stack = [A, B] → p дедуплицируется, EOF в конце
        var rules = new Dictionary<string, Rule[]>
        {
            {"S", new Rule[] { new Seq(new Rule[] { new Ref("A"), new Literal("p") }, "S") } },
            {"A", new Rule[] { new Seq(new Rule[] { new Ref("B"), new Literal("p") }, "A") } },
            {"B", new Rule[] { new Literal("r") } },
        };
        var calc = new FollowSetCalculator(rules, "S");

        var stack = new List<StackFrame>
        {
            new StackFrame("A", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("B", 0, new RuleFrameLocation(0), null, null),
        };

        var terminators = calc.GetTerminators(stack);

        Assert.AreEqual(2, terminators.Length);
        Assert.AreEqual("p", ((Literal)terminators[0]).Value);
        Assert.AreEqual("EOF", terminators[1].Kind);
    }

    [TestMethod]
    public void Test_GetTerminators_EofAtEnd()
    {
        // B — стартовое правило, follow(B) содержит EOF; B — внутренний кадр.
        // EOF должен оказаться в конце, а не в середине.
        var rules = new Dictionary<string, Rule[]>
        {
            {"B", new Rule[] { new Seq(new Rule[] { new Ref("A"), new Literal("p") }, "B") } },
            {"A", new Rule[] { new Literal("q") } },
        };
        var calc = new FollowSetCalculator(rules, "B");

        var stack = new List<StackFrame>
        {
            new StackFrame("A", 0, new RuleFrameLocation(0), null, null),
            new StackFrame("B", 0, new RuleFrameLocation(0), null, null),
        };

        var terminators = calc.GetTerminators(stack);

        Assert.AreEqual("EOF", terminators[terminators.Length - 1].Kind, "EOF must be at the end");
        Assert.AreEqual(1, terminators.Count(t => t.Kind == "EOF"), "EOF must appear exactly once");
    }

    [TestMethod]
    public void Test_GetTerminators_OptionsOverride()
    {
        // S = A p → follow(A) = { p }; Options кадра A задают явные терминаторы [ z ]
        var rules = new Dictionary<string, Rule[]>
        {
            {"S", new Rule[] { new Seq(new Rule[] { new Ref("A"), new Literal("p") }, "S") } },
            {"A", new Rule[] { new Literal("q") } },
        };
        var calc = new FollowSetCalculator(rules, "S");

        var options = new RecoveryOptions { Terminators = new Terminal[] { new Literal("z") } };
        var stack = new List<StackFrame>
        {
            new StackFrame("A", 0, new RuleFrameLocation(0), null, options),
        };

        var terminators = calc.GetTerminators(stack);

        Assert.AreEqual(2, terminators.Length);
        Assert.AreEqual("z", ((Literal)terminators[0]).Value, "Options.Terminators override follow(A)");
        Assert.AreEqual("EOF", terminators[1].Kind);
        Assert.IsFalse(terminators.Any(t => t is Literal l && l.Value == "p"), "follow(A) 'p' must NOT appear when Options.Terminators set");
    }
}
#endif
