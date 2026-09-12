#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TerminalMatcher]
public sealed partial class PatchRollbackTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();

    [Regex(@"[^\w\s]+")]
    public static partial RecoveryTerminal ErrorOperator();

    public static Terminal ErrorEmpty() => _error;

    private static Terminal _error = new EmptyTerminal("Error");
}

[TestClass]
public sealed class PatchRollbackTests
{
    // MiniC-подобная грамматика с recovery-правилами (по образцу IterativeRecoveryTests).
    private static Parser NewParser()
    {
        var parser = new Parser(PatchRollbackTerminals.Trivia());
        var closingBrace = new OftenMissed(new Literal("}"));
        var semicolon = new OftenMissed(new Literal(";"));

        parser.Rules["Expr"] = new Rule[]
        {
            PatchRollbackTerminals.Number(),
            PatchRollbackTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment"),

            // Recovery rules:
            new Seq([new Ref("Expr"), PatchRollbackTerminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), PatchRollbackTerminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            PatchRollbackTerminals.ErrorEmpty(),
        };

        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), PatchRollbackTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };

        parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };

        parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), PatchRollbackTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };

        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Снимок состояния (Memo + Injections) в строку для побайтового сравнения.
    private static string SnapshotState(Parser parser) =>
        "MEMO:\n" + string.Join("\n", parser.Memo
            .OrderBy(k => k.Key.pos).ThenBy(k => k.Key.rule).ThenBy(k => k.Key.precedence)
            .Select(k => $"{k.Key.pos}|{k.Key.rule}|{k.Key.precedence}|{k.Value.ResultKind}|{k.Value.NewPos}|{k.Value.MaxFailPos}"))
        + "\nINJ:\n" + string.Join("\n", parser.Injections
            .OrderBy(k => k.Key.Pos).ThenBy(k => k.Key.Terminal.Kind)
            .Select(k => $"{k.Key.Pos}|{k.Key.Terminal.Kind}|{k.Value.Length}|{k.Value.NodeKind}|{k.Value.IsSkip}"));

    // Невосстановимый/восстанавливаемый вход: Parse (memo/injections заполнены), затем снимок/Apply/Rollback.
    private static (Parser Parser, string Input, FailureSnapshot Snapshot, int E, List<RecoveryCandidate> Candidates) Setup(string input)
    {
        var parser = NewParser();
        var result = parser.Parse(input, "Module", out _);
        var snapshot = parser.LastSnapshot;
        if (snapshot is null)
            throw new InvalidOperationException("No snapshot captured for: " + input);
        var e = snapshot.Pos;
        var candidates = RecoveryEngine.Generate(e, snapshot, input, parser, result.ResultKind);
        return (parser, input, snapshot, e, candidates);
    }

    // 1. Откат восстанавливает memo/инъекции побайтово: Apply кандидата → Rollback → состояние идентично снимку.
    [TestMethod]
    public void Test_Rollback_Restores_State_ByteForByte()
    {
        var (parser, input, snapshot, e, candidates) = Setup("int f() { int x int y; }");
        Assert.IsTrue(candidates.Count > 0);
        var candidate = candidates[0];

        var before = SnapshotState(parser);
        var log = parser.ApplyPatches(candidate, e, snapshot, "Module", 0);
        parser.RollbackPatches(log);
        var after = SnapshotState(parser);

        Assert.AreEqual(before, after);
    }

    // 1б. То же для входа, который recovery НЕ восстанавливает до EOF (откат всё равно побайтовый).
    [TestMethod]
    public void Test_Rollback_Restores_State_ByteForByte_Unrecoverable()
    {
        var (parser, input, snapshot, e, candidates) = Setup("int f() { int x $^ int y $^ }");
        Assert.IsTrue(candidates.Count > 0);
        var candidate = candidates[0];

        var before = SnapshotState(parser);
        var log = parser.ApplyPatches(candidate, e, snapshot, "Module", 0);
        parser.RollbackPatches(log);
        var after = SnapshotState(parser);

        Assert.AreEqual(before, after);
    }

    // 2. Префикс не тронут: после Apply кандидата все записи _memo с pos < e без изменений, кроме задокументированных
    //    исключений HygieneCore: Failure (удаляются) и запись start-правила на currentStartPos (удаляется, п. (b)).
    [TestMethod]
    public void Test_Prefix_Untouched_After_Apply()
    {
        const int currentStartPos = 0;
        const string startRule = "Module";
        var (parser, input, snapshot, e, candidates) = Setup("int f() { int x int y; }");
        Assert.IsTrue(candidates.Count > 0);

        // Записи pos < e, кроме Failure и записи start-правила на currentStartPos (оба — задокументированные удаления Hygiene).
        var prefixBefore = parser.Memo
            .Where(k => k.Key.pos < e && k.Value.ResultKind != Result.Kind.Failure
                && !(k.Key.pos == currentStartPos && k.Key.rule == startRule))
            .OrderBy(k => k.Key.pos).ThenBy(k => k.Key.rule).ThenBy(k => k.Key.precedence)
            .Select(k => $"{k.Key.pos}|{k.Key.rule}|{k.Key.precedence}|{k.Value.ResultKind}|{k.Value.NewPos}|{k.Value.MaxFailPos}")
            .ToList();

        var log = parser.ApplyPatches(candidates[0], e, snapshot, startRule, currentStartPos);

        var prefixAfter = parser.Memo
            .Where(k => k.Key.pos < e && k.Value.ResultKind != Result.Kind.Failure
                && !(k.Key.pos == currentStartPos && k.Key.rule == startRule))
            .OrderBy(k => k.Key.pos).ThenBy(k => k.Key.rule).ThenBy(k => k.Key.precedence)
            .Select(k => $"{k.Key.pos}|{k.Key.rule}|{k.Key.precedence}|{k.Value.ResultKind}|{k.Value.NewPos}|{k.Value.MaxFailPos}")
            .ToList();

        CollectionAssert.AreEqual(prefixBefore, prefixAfter);
        parser.RollbackPatches(log);
    }

    // 3. Hygiene: после Apply (S0) Failure на e для правил снимка удалены; Success/Partial на e на месте.
    [TestMethod]
    public void Test_Hygiene_Removes_Failure_At_E_Keeps_Success_Partial()
    {
        var (parser, input, snapshot, e, _) = Setup("int f() { int x int y; }");
        var snapshotRules = snapshot.Stack.Select(f => f.RuleName).Distinct().ToList();

        // До Apply: есть Failure на e для правил снимка.
        var failureAtE = parser.Memo
            .Where(k => k.Key.pos == e && snapshotRules.Contains(k.Key.rule) && k.Value.ResultKind == Result.Kind.Failure)
            .ToList();
        Assert.IsTrue(failureAtE.Count > 0);

        // Success/Partial на e (до Apply).
        var successPartialAtE = parser.Memo
            .Where(k => k.Key.pos == e && k.Value.ResultKind != Result.Kind.Failure)
            .Select(k => $"{k.Key.pos}|{k.Key.rule}|{k.Key.precedence}|{k.Value.ResultKind}|{k.Value.NewPos}|{k.Value.MaxFailPos}")
            .OrderBy(x => x)
            .ToList();

        // Apply S0 (только Hygiene, без патчей).
        var s0 = new RecoveryCandidate("S0", 0, e, 0, "Module", null, _ => { }, _ => { }, []);
        var log = parser.ApplyPatches(s0, e, snapshot, "Module", 0);

        // После Apply: Failure на e для правил снимка удалены.
        var failureAtEAfter = parser.Memo
            .Where(k => k.Key.pos == e && snapshotRules.Contains(k.Key.rule) && k.Value.ResultKind == Result.Kind.Failure)
            .ToList();
        Assert.AreEqual(0, failureAtEAfter.Count);

        // Success/Partial на e на месте (Hygiene их не трогает).
        var successPartialAtEAfter = parser.Memo
            .Where(k => k.Key.pos == e && k.Value.ResultKind != Result.Kind.Failure)
            .Select(k => $"{k.Key.pos}|{k.Key.rule}|{k.Key.precedence}|{k.Value.ResultKind}|{k.Value.NewPos}|{k.Value.MaxFailPos}")
            .OrderBy(x => x)
            .ToList();
        CollectionAssert.AreEqual(successPartialAtE, successPartialAtEAfter);

        parser.RollbackPatches(log);
    }

    // 4. Интеграция S1: вход, где S0 (ре-парсинг как есть) без прогресса, а S1 (вставка) восстанавливает →
    //    Success@EOF, RecoveryDiagnostics содержит Inserted. Пропущена «(» в заголовке функции.
    [TestMethod]
    public void Test_S1_Integration_Insertion_Recovers()
    {
        var parser = NewParser();
        var input = "int f ) { }";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(parser.ErrorInfo);
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Inserted));
    }

    // 5. Отклонённые кандидаты не оставляют следов: после Parse в Injections только эффекты принятых кандидатов
    //    (каждый Inserted/Skipped-диагностике соответствует инъекция; лишних инъекций нет).
    [TestMethod]
    public void Test_Rejected_Candidates_Leave_No_Traces()
    {
        var parser = NewParser();
        var input = "int f ) { }";
        var result = parser.Parse(input, "Module", out _);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end));
        Assert.AreEqual(input.Length, end);

        // Каждый Inserted/Skipped-диагностике соответствует инъекция в том же месте.
        foreach (var d in parser.RecoveryDiagnostics.Where(d => d.Kind is RecoveryKind.Inserted or RecoveryKind.Skipped))
        {
            var hasInjection = parser.Injections.Any(k => k.Key.Pos == d.StartPos);
            Assert.IsTrue(hasInjection, $"No injection at pos {d.StartPos} for diagnostic {d.Kind}@{d.StartPos}");
        }

        // Число инъекций не превышает число принятых диагностик (отклонённые кандидаты откатились).
        var acceptedCount = parser.RecoveryDiagnostics.Count(d => d.Kind is RecoveryKind.Inserted or RecoveryKind.Skipped);
        Assert.IsTrue(parser.Injections.Count <= acceptedCount + 1,
            $"Injections ({parser.Injections.Count}) exceed accepted diagnostics ({acceptedCount}) — rejected candidates left traces");
    }

    // 6. Фантомный баг RecordMemo: SetMemo на НОВЫЙ ключ → Apply → Rollback → ключ должен быть УДАЛЁН,
    //    а не восстановлен как фантомный default(Result) (Success, Node=null).
    [TestMethod]
    public void Test_Rollback_Memo_NewKey_NoPhantom()
    {
        var parser = NewParser();
        const string rule = "PhantomRule";
        const int pos = 5;
        const int prec = 0;
        var value = Result.Success(new TerminalNode("Phantom", pos, pos, 0), pos, pos);

        // Кандидат (S2/S3-подобный), чей Apply делает SetMemo на НОВЫЙ ключ (нет в memo).
        var candidate = new RecoveryCandidate("S2", 1, pos, 0, rule, null,
            p => p.SetMemo(rule, pos, prec, value), _ => { }, []);

        var log = parser.ApplyPatches(candidate, pos, null, "Module", 0);

        // Sanity: после Apply ключ в memo.
        Assert.IsTrue(parser.Memo.ContainsKey((pos, rule, prec)), "Key should be present in Memo after Apply");

        parser.RollbackPatches(log);

        // Ключ должен быть УДАЛЁН, а не фантомный default(Result).
        Assert.IsFalse(parser.Memo.ContainsKey((pos, rule, prec)),
            "Phantom default(Result) left in Memo after rollback of SetMemo on a new key");
    }

    // 7. Hygiene scope (b): запись start-правила на currentStartPos удаляется ЛЮБОГО типа
    //    (Partial / Success < EOF) — устаревший верхнеуровневый результат первого прохода;
    //    запись НЕ-start-правила (не Failure) на той же позиции остаётся.
    [TestMethod]
    public void Test_Hygiene_Removes_StartRule_AnyKind_At_CurrentStartPos()
    {
        var parser = NewParser();
        const int currentStartPos = 0;
        const string startRule = "Module";

        // Записи start-правила на currentStartPos: Partial и Success < EOF (любой тип) → удаляются (scope b).
        parser.SetMemo(startRule, currentStartPos, 0, Result.Partial(new TerminalNode(startRule, 0, 5, 5), 5, 5));
        parser.SetMemo(startRule, currentStartPos, 1, Result.Success(new TerminalNode(startRule, 0, 3, 3), 3, 3));
        // Запись НЕ-start-правила (не Failure) на той же позиции → остаётся.
        parser.SetMemo("Expr", currentStartPos, 0, Result.Success(new TerminalNode("Expr", 0, 2, 2), 2, 2));

        var log = parser.Hygiene(10, null, startRule, currentStartPos);

        Assert.IsFalse(parser.Memo.ContainsKey((currentStartPos, startRule, 0)),
            "Partial start-rule entry at currentStartPos should be removed (scope b)");
        Assert.IsFalse(parser.Memo.ContainsKey((currentStartPos, startRule, 1)),
            "Success<EOF start-rule entry at currentStartPos should be removed (scope b)");
        Assert.IsTrue(parser.Memo.ContainsKey((currentStartPos, "Expr", 0)),
            "Non-start-rule non-Failure entry at currentStartPos should remain");

        parser.RollbackPatches(log);
    }

    // 8. Hygiene scope (c): ВСЕ stale Failure удаляются в любой позиции (не только на e);
    //    Success в позиции ≠ e и ≠ currentStartPos остаётся.
    [TestMethod]
    public void Test_Hygiene_Removes_Stale_Failure_At_Other_Positions()
    {
        var parser = NewParser();
        const int currentStartPos = 0;
        const string startRule = "Module";
        const int e = 10;

        // Stale Failure в позиции ≠ e и ≠ currentStartPos → удаляется (scope c).
        parser.SetMemo("Expr", 5, 0, Result.Failure(5));
        // Success в позиции ≠ e и ≠ currentStartPos → остаётся.
        parser.SetMemo("Statement", 7, 0, Result.Success(new TerminalNode("Statement", 7, 9, 2), 9, 9));

        var log = parser.Hygiene(e, null, startRule, currentStartPos);

        Assert.IsFalse(parser.Memo.ContainsKey((5, "Expr", 0)),
            "Stale Failure at pos != e should be removed (scope c)");
        Assert.IsTrue(parser.Memo.ContainsKey((7, "Statement", 0)),
            "Success at pos != e should remain");

        parser.RollbackPatches(log);
    }

    // 9. E2E: реальный recovery-путь применяет S2/S3-кандидатов (часть принята с прогрессом,
    //    часть отклонена без прогресса). Инвариант: в Memo НЕТ фантомных Success (Node == null,
    //    т.е. default(Result)) и все инъекции осмысленны (не Length==0 && NodeKind=="").
    //    Вход: мусор ### между закрытым блоком и валидной функцией → S2 resync (принят, e 19→27),
    //    S3 panic (принят, e 27→28), S1 вставка (принята, e 28→31); на e=31 S3/S4/S5 отклонены
    //    без прогресса → recovery останавливается (ErrorInfo != null).
    [TestMethod]
    public void Test_Rejected_S2_S3_Candidates_Leave_No_Memo_Phantoms()
    {
        var parser = NewParser();
        // Бюджет: чтобы цикл реально дошёл до S2/S3 (rank 2/3) — по умолчанию
        // MaxRecoveryAttemptsPerPosition=3 → пробуются только S0 + 2×S1.
        parser.MaxRecoveryAttemptsPerPosition = 10;
        var input = "int f() { int x; } ### int g() { int y; }";
        var result = parser.Parse(input, "Module", out _);

        // Сценарий: recovery остановился, не достигнув EOF (финальные S2/S3-кандидаты отклонены без прогресса).
        Assert.IsNotNull(parser.ErrorInfo, "Expected recovery to stop without reaching EOF");
        // S2/S3-кандидаты применялись и принимались (Skipped-диагностика resync/panic).
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Skipped),
            "Expected S2/S3 Skipped diagnostics (resync/panic candidates were applied)");

        // Инвариант: в Memo нет фантомного Success (Node == null, т.е. default(Result)).
        foreach (var kv in parser.Memo)
        {
            if (kv.Value.ResultKind != Result.Kind.Success)
                continue;
            Assert.IsTrue(kv.Value.TryGetSuccess(out var node, out _));
            Assert.IsNotNull(node, $"Phantom Success (Node == null) in Memo at {kv.Key.pos}|{kv.Key.rule}|{kv.Key.precedence}");
        }

        // Инвариант: каждая инъекция осмысленна (не фантом Length==0 && NodeKind=="").
        foreach (var kv in parser.Injections)
            Assert.IsFalse(kv.Value.Length == 0 && kv.Value.NodeKind == "",
                $"Phantom injection at {kv.Key.Pos}|{kv.Key.Terminal.Kind}");
    }
}
