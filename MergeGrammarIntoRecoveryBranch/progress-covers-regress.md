# Covers-Regress Research Report (Success@EOF without tree coverage)

Branch `error-recovery-completion-2`, HEAD `f98745a`. Sample: `C:\RSDN\roslyn\src\Compilers\CSharp\Test\Semantic\Semantics\NullableReferenceTypesTests.cs` (6 748 449 bytes). Harness reproduced the probe exactly: `verdict=Uncovered coversInput=False absorberBytes=1263 recoveryNodes=1 success=True passes=3 memo=3`.

## Intentions

**Goal:** find exactly where `Result.NewPos == input.Length` (Success@EOF) is born while the parse tree's terminal spans cover only ~1263 absorber bytes (`coversInput=False`), under the probe conditions (Cs1..Cs14 grammars, RAW file text, start rule `Grammar`, default IDE profile).

**What was instrumented (temporary `Trace.WriteLine`, marked `// TEMP-COVERS-RESEARCH`):**

1. `ExtensibleParser/Parser.Recovery.cs`
   - `Recover`: per-pass result after each `ParseRule`; every exit point (`end == input.Length` checks at original lines 681/892, iteration-limit return at 711, loop-exhaustion return at 899).
   - `TryCandidate`: every candidate attempt (`Id, Rank, Pos, Cost, RuleName, TerminalKind`), the re-parse result (`next`, `e`, `e2`, `ErrorPos`), and the accept/reject decision with branch taken (`success-at-EOF` / `progress` / `no-progress` / `already-tried` / `tier-budget`).
   - Full generated candidate lists after `RecoveryEngine.Generate` (normal + `ForceS6` paths).
   - `FinishRecovery` (line 909): entry result + returned node Kind+span.
   - `SetMemo` (line 311) / `PatchMemo` (line 329): every recovery memo patch.
2. `ExtensibleParser/Parser.cs`
   - `ParseRule` memo hit when `cached.NewPos == input.Length`; engine memo write when `result.NewPos == input.Length` (gated to EOF to avoid flooding).
3. `ExtensibleParser/Recovery/RecoveryEngine.cs`
   - `GenerateS6`: `e, parseEnd, s, foundT, start, prefixNode` (type+span), resulting `node` (type+span), `value.NewPos`, `currentStartPos` — the direct H1 probe.

**Snapshot/restore plan (executed):** before editing, the three production files were copied to `%TEMP%\opencode\covers-research\orig\` (the `Parser.cs` original was taken byte-exact from `git show HEAD:ExtensibleParser/Parser.cs` since it was instrumented after the initial snapshot). Before finishing, they were copied back byte-exact; SHA256 verified identical, `git diff --stat` EMPTY, `git status --short` shows only the two untracked entries (see Restore verification).

**Harness (no production behavior change beyond traces):** console project `net8.0` at `%TEMP%\opencode\covers-research\harness\` with `ProjectReference` to `C:\RSDN\CsNitra\Parsers\CSharp\CSharpGrammar\CSharpGrammar.csproj`. Loads Cs1..Cs14 grammar texts via reflection from `typeof(CSharpParser).Assembly` (`GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal))`, suffixes `Cs1.grammar`..`Cs14.grammar`, ascending), builds a FRESH `CSharpParser(grammars, CSharpTerminals.Trivia(), CSharpTerminals.GetAll())`, parses the sample file RAW (`File.ReadAllText`), start rule `"Grammar"`, default profile — exactly the probe's conditions. The probe test file was NOT touched and NOT run; the harness replaces it. A `ConsoleTraceListener` was installed so the TEMP traces land in the same captured stream.

**Dump captured to `%TEMP%\opencode\covers-research\dump.log` (450 lines), containing:** (1) all `Memo` entries; (2) full result-tree walk (`RawElements`/`Delimiters`/`SomeNode.Value`; node types in `SyntaxTree.cs`: `TerminalNode`, `SeqNode`, `ListNode`, `SomeNode`/`NoneNode` (under `OptionalNode`), `PredicateNode`) with `IsRecovery`/`IsAbsorber` marks and coverage totals; (3) all `RecoveryDiagnostics`; (4) `RecoveryPasses`/`EngineGenerateCalls`/`DegradationLevel`; (5) the TEMP-COVERS-RESEARCH traces.

**Hypotheses tested:**
- H1: `GenerateS6` builds `prefixNode` with `EndPos = s < input.Length`, accepted candidate is `Success@s`, and final `Success@EOF` comes from somewhere else — find where.
- H2: after accepting S6 at S, `Recover` loops (`currentStartPos = S`), later S6 passes reach EOF but do NOT extend the tree (start-rule memo already patched) — the final node loses earlier content.
- H3: `FinishRecovery`/finalization declares success by a position other than `Result.NewPos` (e.g. `MaxFailPos == input.Length`).

## Method

- Instrumentation points: as listed in Intentions (all marked `// TEMP-COVERS-RESEARCH`; `Trace.WriteLine` is not `[Conditional]`-stripped, so traces fire in any build configuration).
- Harness: `CoversHarness.csproj` + `Program.cs` (see above); run `dotnet run` with all output redirected to `dump.log`. Parse elapsed ≈ 3.9 s.
- Cross-check: the harness recomputes `ParseHealth.Analyze` and got the probe's exact numbers (`Uncovered`, `coversInput=False`, `absorberBytes=1263`, `recoveryNodes=1`, `passes=3`, `memo=3`).
- Note on the dump: the first `TEMP-COVERS-RECOVER` block (`inputLen=254584`) is the **grammar-construction** parse (CsNitraParser parsing the merged Cs1..Cs14 grammar text during `CSharpParser` construction) — a different `Parser` instance, clean success, irrelevant to the sample parse. The sample parse starts at `TEMP-COVERS-RECOVER iter=0 currentStartPos=208`.

## Raw dump (verbatim)

The full `dump.log` (450 lines) is embedded below. Items (1)–(4) are the harness's post-parse dumps; item (5) is the interleaved `TEMP-COVERS-*` trace stream.

```
=== HARNESS === input=C:\RSDN\roslyn\src\Compilers\CSharp\Test\Semantic\Semantics\NullableReferenceTypesTests.cs length=6748449
TEMP-COVERS-MEMOWRITE-EOF key=(254553,Statement,0) Success NewPos=254584 MaxFailPos=254584 node=SeqNode(Rule)[254553..254584)
TEMP-COVERS-MEMOWRITE-EOF key=(0,Grammar,0) Success NewPos=254584 MaxFailPos=254584 node=SeqNode(Grammar)[0..254584)
TEMP-COVERS-RECOVER iter=0 currentStartPos=0 result=Success NewPos=254584 MaxFailPos=254584 node=SeqNode(Grammar)[0..254584)
TEMP-COVERS-RECOVER-EXIT-A iter=0 end=254584 inputLen=254584 MaxFailPos=254584 Success NewPos=254584 MaxFailPos=254584 node=SeqNode(Grammar)[0..254584)
TEMP-COVERS-FINISH-ENTRY Success NewPos=254584 MaxFailPos=254584 node=SeqNode(Grammar)[0..254584) inputLen=254584 diagCache=0
TEMP-COVERS-FINISH-EXIT success node=SeqNode(Grammar)[0..254584) finalDiags=0
TEMP-COVERS-RECOVER iter=0 currentStartPos=208 result=Failure NewPos=-1 MaxFailPos=208
TEMP-COVERS-TRY id=S0 rank=0 pos=208 cost=0 rule=Grammar term=- e=208
TEMP-COVERS-REPARSE id=S0 next=Failure NewPos=-1 MaxFailPos=208 e=208 e2=208 ErrorPos=208
TEMP-COVERS-REJECT id=S0 reason=no-progress e2=208 <= e=208
TEMP-COVERS-S6 e=208 parseEnd=0 s=1471 foundT=CloseBrace start=208 prefixNode=null node=TerminalNode(Skipped)[208..1471) valueNewPos=1471 currentStartPos=208
TEMP-COVERS-GENERATE iter=0 e=208 parseEnd=0 candidates=27
TEMP-COVERS-CAND id=S1:UsingDirective:Class rank=1 pos=208 cost=1 rule=UsingDirective term=Class
TEMP-COVERS-CAND id=S1:UsingDirective:CloseBrace rank=1 pos=208 cost=1 rule=UsingDirective term=CloseBrace
TEMP-COVERS-CAND id=S1:UsingDirective:Delegate rank=1 pos=208 cost=1 rule=UsingDirective term=Delegate
TEMP-COVERS-CAND id=S1:UsingDirective:Enum rank=1 pos=208 cost=1 rule=UsingDirective term=Enum
TEMP-COVERS-CAND id=S1:UsingDirective:Extension rank=1 pos=208 cost=1 rule=UsingDirective term=Extension
TEMP-COVERS-CAND id=S1:UsingDirective:Extern rank=1 pos=208 cost=1 rule=UsingDirective term=Extern
TEMP-COVERS-CAND id=S1:UsingDirective:Global rank=1 pos=208 cost=1 rule=UsingDirective term=Global
TEMP-COVERS-CAND id=S1:UsingDirective:Interface rank=1 pos=208 cost=1 rule=UsingDirective term=Interface
TEMP-COVERS-CAND id=S1:UsingDirective:Namespace rank=1 pos=208 cost=1 rule=UsingDirective term=Namespace
TEMP-COVERS-CAND id=S1:UsingDirective:OpenBracket rank=1 pos=208 cost=1 rule=UsingDirective term=OpenBracket
TEMP-COVERS-CAND id=S1:UsingDirective:Record rank=1 pos=208 cost=1 rule=UsingDirective term=Record
TEMP-COVERS-CAND id=S1:UsingDirective:Struct rank=1 pos=208 cost=1 rule=UsingDirective term=Struct
TEMP-COVERS-CAND id=S1:UsingDirective:Using rank=1 pos=208 cost=1 rule=UsingDirective term=Using
TEMP-COVERS-CAND id=S1:UsingDirective:abstract rank=1 pos=208 cost=1 rule=UsingDirective term=abstract
TEMP-COVERS-CAND id=S1:UsingDirective:internal rank=1 pos=208 cost=1 rule=UsingDirective term=internal
TEMP-COVERS-CAND id=S1:UsingDirective:partial rank=1 pos=208 cost=1 rule=UsingDirective term=partial
TEMP-COVERS-CAND id=S1:UsingDirective:private rank=1 pos=208 cost=1 rule=UsingDirective term=private
TEMP-COVERS-CAND id=S1:UsingDirective:protected rank=1 pos=208 cost=1 rule=UsingDirective term=protected
TEMP-COVERS-CAND id=S1:UsingDirective:public rank=1 pos=208 cost=1 rule=UsingDirective term=public
TEMP-COVERS-CAND id=S1:UsingDirective:readonly rank=1 pos=208 cost=1 rule=UsingDirective term=readonly
TEMP-COVERS-CAND id=S1:UsingDirective:ref rank=1 pos=208 cost=1 rule=UsingDirective term=ref
TEMP-COVERS-CAND id=S1:UsingDirective:sealed rank=1 pos=208 cost=1 rule=UsingDirective term=sealed
TEMP-COVERS-CAND id=S1:UsingDirective:static rank=1 pos=208 cost=1 rule=UsingDirective term=static
TEMP-COVERS-CAND id=S1:UsingDirective:unsafe rank=1 pos=208 cost=1 rule=UsingDirective term=unsafe
TEMP-COVERS-CAND id=S2:UsingDirective:T1:229 rank=2 pos=229 cost=7 rule=UsingDirective term=NamespaceMember
TEMP-COVERS-CAND id=S4:UsingDirective:EOF rank=4 pos=6748449 cost=4 rule=UsingDirective term=-
TEMP-COVERS-CAND id=S6:Grammar:CloseBrace rank=6 pos=1471 cost=174 rule=Grammar term=CloseBrace
TEMP-COVERS-TRY id=S1:UsingDirective:Class rank=1 pos=208 cost=1 rule=UsingDirective term=Class e=208
TEMP-COVERS-REPARSE id=S1:UsingDirective:Class next=Failure NewPos=-1 MaxFailPos=208 e=208 e2=208 ErrorPos=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Class reason=no-progress e2=208 <= e=208
TEMP-COVERS-TRY id=S1:UsingDirective:CloseBrace rank=1 pos=208 cost=1 rule=UsingDirective term=CloseBrace e=208
TEMP-COVERS-REPARSE id=S1:UsingDirective:CloseBrace next=Failure NewPos=-1 MaxFailPos=208 e=208 e2=208 ErrorPos=208
TEMP-COVERS-REJECT id=S1:UsingDirective:CloseBrace reason=no-progress e2=208 <= e=208
TEMP-COVERS-TRY id=S1:UsingDirective:Delegate rank=1 pos=208 cost=1 rule=UsingDirective term=Delegate e=208
TEMP-COVERS-REPARSE id=S1:UsingDirective:Delegate next=Failure NewPos=-1 MaxFailPos=208 e=208 e2=208 ErrorPos=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Delegate reason=no-progress e2=208 <= e=208
TEMP-COVERS-TRY id=S1:UsingDirective:Enum rank=1 pos=208 cost=1 rule=UsingDirective term=Enum e=208
TEMP-COVERS-REPARSE id=S1:UsingDirective:Enum next=Failure NewPos=-1 MaxFailPos=208 e=208 e2=208 ErrorPos=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Enum reason=no-progress e2=208 <= e=208
TEMP-COVERS-TRY id=S1:UsingDirective:Extension rank=1 pos=208 cost=1 rule=UsingDirective term=Extension e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Extension reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:Extern rank=1 pos=208 cost=1 rule=UsingDirective term=Extern e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Extern reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:Global rank=1 pos=208 cost=1 rule=UsingDirective term=Global e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Global reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:Interface rank=1 pos=208 cost=1 rule=UsingDirective term=Interface e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Interface reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:Namespace rank=1 pos=208 cost=1 rule=UsingDirective term=Namespace e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Namespace reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:OpenBracket rank=1 pos=208 cost=1 rule=UsingDirective term=OpenBracket e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:OpenBracket reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:Record rank=1 pos=208 cost=1 rule=UsingDirective term=Record e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Record reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:Struct rank=1 pos=208 cost=1 rule=UsingDirective term=Struct e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Struct reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:Using rank=1 pos=208 cost=1 rule=UsingDirective term=Using e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:Using reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:abstract rank=1 pos=208 cost=1 rule=UsingDirective term=abstract e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:abstract reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:internal rank=1 pos=208 cost=1 rule=UsingDirective term=internal e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:internal reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:partial rank=1 pos=208 cost=1 rule=UsingDirective term=partial e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:partial reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:private rank=1 pos=208 cost=1 rule=UsingDirective term=private e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:private reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:protected rank=1 pos=208 cost=1 rule=UsingDirective term=protected e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:protected reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:public rank=1 pos=208 cost=1 rule=UsingDirective term=public e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:public reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:readonly rank=1 pos=208 cost=1 rule=UsingDirective term=readonly e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:readonly reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:ref rank=1 pos=208 cost=1 rule=UsingDirective term=ref e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:ref reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:sealed rank=1 pos=208 cost=1 rule=UsingDirective term=sealed e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:sealed reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:static rank=1 pos=208 cost=1 rule=UsingDirective term=static e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:static reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingDirective:unsafe rank=1 pos=208 cost=1 rule=UsingDirective term=unsafe e=208
TEMP-COVERS-REJECT id=S1:UsingDirective:unsafe reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S2:UsingDirective:T1:229 rank=2 pos=229 cost=7 rule=UsingDirective term=NamespaceMember e=208
TEMP-COVERS-REPARSE id=S2:UsingDirective:T1:229 next=Failure NewPos=-1 MaxFailPos=229 e=208 e2=229 ErrorPos=229
TEMP-COVERS-ACCEPT id=S2:UsingDirective:T1:229 branch=progress e2=229 > e=208
TEMP-COVERS-RECOVER iter=1 currentStartPos=208 result=Failure NewPos=-1 MaxFailPos=229
TEMP-COVERS-TRY id=S0 rank=0 pos=229 cost=0 rule=Grammar term=- e=229
TEMP-COVERS-REPARSE id=S0 next=Failure NewPos=-1 MaxFailPos=229 e=229 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S0 reason=no-progress e2=229 <= e=229
TEMP-COVERS-S6 e=229 parseEnd=0 s=1471 foundT=CloseBrace start=208 prefixNode=null node=TerminalNode(Skipped)[208..1471) valueNewPos=1471 currentStartPos=208
TEMP-COVERS-GENERATE iter=1 e=229 parseEnd=0 candidates=101
TEMP-COVERS-CAND id=S1:UsingStatement:$@" rank=1 pos=229 cost=1 rule=UsingStatement term=$@"
TEMP-COVERS-CAND id=S1:UsingStatement:@$" rank=1 pos=229 cost=1 rule=UsingStatement term=@$"
TEMP-COVERS-CAND id=S1:UsingStatement:Await rank=1 pos=229 cost=1 rule=UsingStatement term=Await
TEMP-COVERS-CAND id=S1:UsingStatement:Base rank=1 pos=229 cost=1 rule=UsingStatement term=Base
TEMP-COVERS-CAND id=S1:UsingStatement:BinaryIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=BinaryIntegerLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:Break rank=1 pos=229 cost=1 rule=UsingStatement term=Break
TEMP-COVERS-CAND id=S1:UsingStatement:Case rank=1 pos=229 cost=1 rule=UsingStatement term=Case
TEMP-COVERS-CAND id=S1:UsingStatement:CharLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=CharLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:Checked rank=1 pos=229 cost=1 rule=UsingStatement term=Checked
TEMP-COVERS-CAND id=S1:UsingStatement:Class rank=1 pos=229 cost=1 rule=UsingStatement term=Class
TEMP-COVERS-CAND id=S1:UsingStatement:CloseBrace rank=1 pos=229 cost=1 rule=UsingStatement term=CloseBrace
TEMP-COVERS-CAND id=S1:UsingStatement:Continue rank=1 pos=229 cost=1 rule=UsingStatement term=Continue
TEMP-COVERS-CAND id=S1:UsingStatement:DecimalIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=DecimalIntegerLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:DecimalRealLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=DecimalRealLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:Decrement rank=1 pos=229 cost=1 rule=UsingStatement term=Decrement
TEMP-COVERS-CAND id=S1:UsingStatement:Default rank=1 pos=229 cost=1 rule=UsingStatement term=Default
TEMP-COVERS-CAND id=S1:UsingStatement:Delegate rank=1 pos=229 cost=1 rule=UsingStatement term=Delegate
TEMP-COVERS-CAND id=S1:UsingStatement:Do rank=1 pos=229 cost=1 rule=UsingStatement term=Do
TEMP-COVERS-CAND id=S1:UsingStatement:Else rank=1 pos=229 cost=1 rule=UsingStatement term=Else
TEMP-COVERS-CAND id=S1:UsingStatement:EmptyStatement rank=1 pos=229 cost=1 rule=UsingStatement term=EmptyStatement
TEMP-COVERS-CAND id=S1:UsingStatement:Enum rank=1 pos=229 cost=1 rule=UsingStatement term=Enum
TEMP-COVERS-CAND id=S1:UsingStatement:Extension rank=1 pos=229 cost=1 rule=UsingStatement term=Extension
TEMP-COVERS-CAND id=S1:UsingStatement:Extern rank=1 pos=229 cost=1 rule=UsingStatement term=Extern
TEMP-COVERS-CAND id=S1:UsingStatement:FieldExpr rank=1 pos=229 cost=1 rule=UsingStatement term=FieldExpr
TEMP-COVERS-CAND id=S1:UsingStatement:For rank=1 pos=229 cost=1 rule=UsingStatement term=For
TEMP-COVERS-CAND id=S1:UsingStatement:Foreach rank=1 pos=229 cost=1 rule=UsingStatement term=Foreach
TEMP-COVERS-CAND id=S1:UsingStatement:From rank=1 pos=229 cost=1 rule=UsingStatement term=From
TEMP-COVERS-CAND id=S1:UsingStatement:Global rank=1 pos=229 cost=1 rule=UsingStatement term=Global
TEMP-COVERS-CAND id=S1:UsingStatement:Goto rank=1 pos=229 cost=1 rule=UsingStatement term=Goto
TEMP-COVERS-CAND id=S1:UsingStatement:HexIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=HexIntegerLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:If rank=1 pos=229 cost=1 rule=UsingStatement term=If
TEMP-COVERS-CAND id=S1:UsingStatement:Increment rank=1 pos=229 cost=1 rule=UsingStatement term=Increment
TEMP-COVERS-CAND id=S1:UsingStatement:Interface rank=1 pos=229 cost=1 rule=UsingStatement term=Interface
TEMP-COVERS-CAND id=S1:UsingStatement:InterpolatedString rank=1 pos=229 cost=1 rule=UsingStatement term=InterpolatedString
TEMP-COVERS-CAND id=S1:UsingStatement:Lock rank=1 pos=229 cost=1 rule=UsingStatement term=Lock
TEMP-COVERS-CAND id=S1:UsingStatement:Minus rank=1 pos=229 cost=1 rule=UsingStatement term=Minus
TEMP-COVERS-CAND id=S1:UsingStatement:Nameof rank=1 pos=229 cost=1 rule=UsingStatement term=Nameof
TEMP-COVERS-CAND id=S1:UsingStatement:Namespace rank=1 pos=229 cost=1 rule=UsingStatement term=Namespace
TEMP-COVERS-CAND id=S1:UsingStatement:New rank=1 pos=229 cost=1 rule=UsingStatement term=New
TEMP-COVERS-CAND id=S1:UsingStatement:Not rank=1 pos=229 cost=1 rule=UsingStatement term=Not
TEMP-COVERS-CAND id=S1:UsingStatement:OpenBrace rank=1 pos=229 cost=1 rule=UsingStatement term=OpenBrace
TEMP-COVERS-CAND id=S1:UsingStatement:OpenBracket rank=1 pos=229 cost=1 rule=UsingStatement term=OpenBracket
TEMP-COVERS-CAND id=S1:UsingStatement:OpenParen rank=1 pos=229 cost=1 rule=UsingStatement term=OpenParen
TEMP-COVERS-CAND id=S1:UsingStatement:Plus rank=1 pos=229 cost=1 rule=UsingStatement term=Plus
TEMP-COVERS-CAND id=S1:UsingStatement:Quote rank=1 pos=229 cost=1 rule=UsingStatement term=Quote
TEMP-COVERS-CAND id=S1:UsingStatement:Range rank=1 pos=229 cost=1 rule=UsingStatement term=Range
TEMP-COVERS-CAND id=S1:UsingStatement:RawString rank=1 pos=229 cost=1 rule=UsingStatement term=RawString
TEMP-COVERS-CAND id=S1:UsingStatement:Record rank=1 pos=229 cost=1 rule=UsingStatement term=Record
TEMP-COVERS-CAND id=S1:UsingStatement:Ref rank=1 pos=229 cost=1 rule=UsingStatement term=Ref
TEMP-COVERS-CAND id=S1:UsingStatement:Return rank=1 pos=229 cost=1 rule=UsingStatement term=Return
TEMP-COVERS-CAND id=S1:UsingStatement:SeparatedBinaryIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedBinaryIntegerLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:SeparatedDecimalIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedDecimalIntegerLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:SeparatedDecimalRealLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedDecimalRealLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:SeparatedHexIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedHexIntegerLiteral
TEMP-COVERS-CAND id=S1:UsingStatement:Sizeof rank=1 pos=229 cost=1 rule=UsingStatement term=Sizeof
TEMP-COVERS-CAND id=S1:UsingStatement:Struct rank=1 pos=229 cost=1 rule=UsingStatement term=Struct
TEMP-COVERS-CAND id=S1:UsingStatement:Switch rank=1 pos=229 cost=1 rule=UsingStatement term=Switch
TEMP-COVERS-CAND id=S1:UsingStatement:Throw rank=1 pos=229 cost=1 rule=UsingStatement term=Throw
TEMP-COVERS-CAND id=S1:UsingStatement:Tilde rank=1 pos=229 cost=1 rule=UsingStatement term=Tilde
TEMP-COVERS-CAND id=S1:UsingStatement:Try rank=1 pos=229 cost=1 rule=UsingStatement term=Try
TEMP-COVERS-CAND id=S1:UsingStatement:Typeof rank=1 pos=229 cost=1 rule=UsingStatement term=Typeof
TEMP-COVERS-CAND id=S1:UsingStatement:Unchecked rank=1 pos=229 cost=1 rule=UsingStatement term=Unchecked
TEMP-COVERS-CAND id=S1:UsingStatement:Var rank=1 pos=229 cost=1 rule=UsingStatement term=Var
TEMP-COVERS-CAND id=S1:UsingStatement:VerbatimString rank=1 pos=229 cost=1 rule=UsingStatement term=VerbatimString
TEMP-COVERS-CAND id=S1:UsingStatement:While rank=1 pos=229 cost=1 rule=UsingStatement term=While
TEMP-COVERS-CAND id=S1:UsingStatement:Xor rank=1 pos=229 cost=1 rule=UsingStatement term=Xor
TEMP-COVERS-CAND id=S1:UsingStatement:abstract rank=1 pos=229 cost=1 rule=UsingStatement term=abstract
TEMP-COVERS-CAND id=S1:UsingStatement:async rank=1 pos=229 cost=1 rule=UsingStatement term=async
TEMP-COVERS-CAND id=S1:UsingStatement:bool rank=1 pos=229 cost=1 rule=UsingStatement term=bool
TEMP-COVERS-CAND id=S1:UsingStatement:byte rank=1 pos=229 cost=1 rule=UsingStatement term=byte
TEMP-COVERS-CAND id=S1:UsingStatement:char rank=1 pos=229 cost=1 rule=UsingStatement term=char
TEMP-COVERS-CAND id=S1:UsingStatement:decimal rank=1 pos=229 cost=1 rule=UsingStatement term=decimal
TEMP-COVERS-CAND id=S1:UsingStatement:double rank=1 pos=229 cost=1 rule=UsingStatement term=double
TEMP-COVERS-CAND id=S1:UsingStatement:dynamic rank=1 pos=229 cost=1 rule=UsingStatement term=dynamic
TEMP-COVERS-CAND id=S1:UsingStatement:false rank=1 pos=229 cost=1 rule=UsingStatement term=false
TEMP-COVERS-CAND id=S1:UsingStatement:float rank=1 pos=229 cost=1 rule=UsingStatement term=float
TEMP-COVERS-CAND id=S1:UsingStatement:int rank=1 pos=229 cost=1 rule=UsingStatement term=int
TEMP-COVERS-CAND id=S1:UsingStatement:internal rank=1 pos=229 cost=1 rule=UsingStatement term=internal
TEMP-COVERS-CAND id=S1:UsingStatement:long rank=1 pos=229 cost=1 rule=UsingStatement term=long
TEMP-COVERS-CAND id=S1:UsingStatement:null rank=1 pos=229 cost=1 rule=UsingStatement term=null
TEMP-COVERS-CAND id=S1:UsingStatement:object rank=1 pos=229 cost=1 rule=UsingStatement term=object
TEMP-COVERS-CAND id=S1:UsingStatement:partial rank=1 pos=229 cost=1 rule=UsingStatement term=partial
TEMP-COVERS-CAND id=S1:UsingStatement:private rank=1 pos=229 cost=1 rule=UsingStatement term=private
TEMP-COVERS-CAND id=S1:UsingStatement:protected rank=1 pos=229 cost=1 rule=UsingStatement term=protected
TEMP-COVERS-CAND id=S1:UsingStatement:public rank=1 pos=229 cost=1 rule=UsingStatement term=public
TEMP-COVERS-CAND id=S1:UsingStatement:readonly rank=1 pos=229 cost=1 rule=UsingStatement term=readonly
TEMP-COVERS-CAND id=S1:UsingStatement:sbyte rank=1 pos=229 cost=1 rule=UsingStatement term=sbyte
TEMP-COVERS-CAND id=S1:UsingStatement:sealed rank=1 pos=229 cost=1 rule=UsingStatement term=sealed
TEMP-COVERS-CAND id=S1:UsingStatement:short rank=1 pos=229 cost=1 rule=UsingStatement term=short
TEMP-COVERS-CAND id=S1:UsingStatement:static rank=1 pos=229 cost=1 rule=UsingStatement term=static
TEMP-COVERS-CAND id=S1:UsingStatement:string rank=1 pos=229 cost=1 rule=UsingStatement term=string
TEMP-COVERS-CAND id=S1:UsingStatement:this rank=1 pos=229 cost=1 rule=UsingStatement term=this
TEMP-COVERS-CAND id=S1:UsingStatement:true rank=1 pos=229 cost=1 rule=UsingStatement term=true
TEMP-COVERS-CAND id=S1:UsingStatement:uint rank=1 pos=229 cost=1 rule=UsingStatement term=uint
TEMP-COVERS-CAND id=S1:UsingStatement:ulong rank=1 pos=229 cost=1 rule=UsingStatement term=ulong
TEMP-COVERS-CAND id=S1:UsingStatement:unsafe rank=1 pos=229 cost=1 rule=UsingStatement term=unsafe
TEMP-COVERS-CAND id=S1:UsingStatement:ushort rank=1 pos=229 cost=1 rule=UsingStatement term=ushort
TEMP-COVERS-CAND id=S1:UsingStatement:void rank=1 pos=229 cost=1 rule=UsingStatement term=void
TEMP-COVERS-CAND id=S2:UsingStatement:T1:229 rank=2 pos=229 cost=1 rule=UsingStatement term=NamespaceMember
TEMP-COVERS-CAND id=S4:UsingStatement:EOF rank=4 pos=6748449 cost=142 rule=UsingStatement term=-
TEMP-COVERS-CAND id=S6:Grammar:CloseBrace rank=6 pos=1471 cost=168 rule=Grammar term=CloseBrace
TEMP-COVERS-TRY id=S1:UsingStatement:$@" rank=1 pos=229 cost=1 rule=UsingStatement term=$@" e=229
TEMP-COVERS-REPARSE id=S1:UsingStatement:$@" next=Failure NewPos=-1 MaxFailPos=229 e=229 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S1:UsingStatement:$@" reason=no-progress e2=229 <= e=229
TEMP-COVERS-TRY id=S1:UsingStatement:@$" rank=1 pos=229 cost=1 rule=UsingStatement term=@$" e=229
TEMP-COVERS-REPARSE id=S1:UsingStatement:@$" next=Failure NewPos=-1 MaxFailPos=229 e=229 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S1:UsingStatement:@$" reason=no-progress e2=229 <= e=229
TEMP-COVERS-TRY id=S1:UsingStatement:Await rank=1 pos=229 cost=1 rule=UsingStatement term=Await e=229
TEMP-COVERS-REPARSE id=S1:UsingStatement:Await next=Failure NewPos=-1 MaxFailPos=229 e=229 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Await reason=no-progress e2=229 <= e=229
TEMP-COVERS-TRY id=S1:UsingStatement:Base rank=1 pos=229 cost=1 rule=UsingStatement term=Base e=229
TEMP-COVERS-REPARSE id=S1:UsingStatement:Base next=Failure NewPos=-1 MaxFailPos=229 e=229 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Base reason=no-progress e2=229 <= e=229
TEMP-COVERS-TRY id=S1:UsingStatement:BinaryIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=BinaryIntegerLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:BinaryIntegerLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Break rank=1 pos=229 cost=1 rule=UsingStatement term=Break e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Break reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Case rank=1 pos=229 cost=1 rule=UsingStatement term=Case e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Case reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:CharLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=CharLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:CharLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Checked rank=1 pos=229 cost=1 rule=UsingStatement term=Checked e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Checked reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Class rank=1 pos=229 cost=1 rule=UsingStatement term=Class e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Class reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:CloseBrace rank=1 pos=229 cost=1 rule=UsingStatement term=CloseBrace e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:CloseBrace reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Continue rank=1 pos=229 cost=1 rule=UsingStatement term=Continue e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Continue reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:DecimalIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=DecimalIntegerLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:DecimalIntegerLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:DecimalRealLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=DecimalRealLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:DecimalRealLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Decrement rank=1 pos=229 cost=1 rule=UsingStatement term=Decrement e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Decrement reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Default rank=1 pos=229 cost=1 rule=UsingStatement term=Default e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Default reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Delegate rank=1 pos=229 cost=1 rule=UsingStatement term=Delegate e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Delegate reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Do rank=1 pos=229 cost=1 rule=UsingStatement term=Do e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Do reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Else rank=1 pos=229 cost=1 rule=UsingStatement term=Else e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Else reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:EmptyStatement rank=1 pos=229 cost=1 rule=UsingStatement term=EmptyStatement e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:EmptyStatement reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Enum rank=1 pos=229 cost=1 rule=UsingStatement term=Enum e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Enum reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Extension rank=1 pos=229 cost=1 rule=UsingStatement term=Extension e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Extension reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Extern rank=1 pos=229 cost=1 rule=UsingStatement term=Extern e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Extern reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:FieldExpr rank=1 pos=229 cost=1 rule=UsingStatement term=FieldExpr e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:FieldExpr reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:For rank=1 pos=229 cost=1 rule=UsingStatement term=For e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:For reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Foreach rank=1 pos=229 cost=1 rule=UsingStatement term=Foreach e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Foreach reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:From rank=1 pos=229 cost=1 rule=UsingStatement term=From e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:From reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Global rank=1 pos=229 cost=1 rule=UsingStatement term=Global e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Global reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Goto rank=1 pos=229 cost=1 rule=UsingStatement term=Goto e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Goto reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:HexIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=HexIntegerLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:HexIntegerLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:If rank=1 pos=229 cost=1 rule=UsingStatement term=If e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:If reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Increment rank=1 pos=229 cost=1 rule=UsingStatement term=Increment e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Increment reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Interface rank=1 pos=229 cost=1 rule=UsingStatement term=Interface e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Interface reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:InterpolatedString rank=1 pos=229 cost=1 rule=UsingStatement term=InterpolatedString e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:InterpolatedString reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Lock rank=1 pos=229 cost=1 rule=UsingStatement term=Lock e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Lock reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Minus rank=1 pos=229 cost=1 rule=UsingStatement term=Minus e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Minus reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Nameof rank=1 pos=229 cost=1 rule=UsingStatement term=Nameof e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Nameof reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Namespace rank=1 pos=229 cost=1 rule=UsingStatement term=Namespace e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Namespace reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:New rank=1 pos=229 cost=1 rule=UsingStatement term=New e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:New reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Not rank=1 pos=229 cost=1 rule=UsingStatement term=Not e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Not reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:OpenBrace rank=1 pos=229 cost=1 rule=UsingStatement term=OpenBrace e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:OpenBrace reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:OpenBracket rank=1 pos=229 cost=1 rule=UsingStatement term=OpenBracket e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:OpenBracket reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:OpenParen rank=1 pos=229 cost=1 rule=UsingStatement term=OpenParen e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:OpenParen reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Plus rank=1 pos=229 cost=1 rule=UsingStatement term=Plus e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Plus reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Quote rank=1 pos=229 cost=1 rule=UsingStatement term=Quote e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Quote reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Range rank=1 pos=229 cost=1 rule=UsingStatement term=Range e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Range reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:RawString rank=1 pos=229 cost=1 rule=UsingStatement term=RawString e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:RawString reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Record rank=1 pos=229 cost=1 rule=UsingStatement term=Record e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Record reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Ref rank=1 pos=229 cost=1 rule=UsingStatement term=Ref e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Ref reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Return rank=1 pos=229 cost=1 rule=UsingStatement term=Return e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Return reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:SeparatedBinaryIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedBinaryIntegerLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:SeparatedBinaryIntegerLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:SeparatedDecimalIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedDecimalIntegerLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:SeparatedDecimalIntegerLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:SeparatedDecimalRealLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedDecimalRealLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:SeparatedDecimalRealLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:SeparatedHexIntegerLiteral rank=1 pos=229 cost=1 rule=UsingStatement term=SeparatedHexIntegerLiteral e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:SeparatedHexIntegerLiteral reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Sizeof rank=1 pos=229 cost=1 rule=UsingStatement term=Sizeof e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Sizeof reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Struct rank=1 pos=229 cost=1 rule=UsingStatement term=Struct e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Struct reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Switch rank=1 pos=229 cost=1 rule=UsingStatement term=Switch e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Switch reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Throw rank=1 pos=229 cost=1 rule=UsingStatement term=Throw e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Throw reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Tilde rank=1 pos=229 cost=1 rule=UsingStatement term=Tilde e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Tilde reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Try rank=1 pos=229 cost=1 rule=UsingStatement term=Try e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Try reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Typeof rank=1 pos=229 cost=1 rule=UsingStatement term=Typeof e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Typeof reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Unchecked rank=1 pos=229 cost=1 rule=UsingStatement term=Unchecked e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Unchecked reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Var rank=1 pos=229 cost=1 rule=UsingStatement term=Var e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Var reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:VerbatimString rank=1 pos=229 cost=1 rule=UsingStatement term=VerbatimString e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:VerbatimString reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:While rank=1 pos=229 cost=1 rule=UsingStatement term=While e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:While reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:Xor rank=1 pos=229 cost=1 rule=UsingStatement term=Xor e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:Xor reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:abstract rank=1 pos=229 cost=1 rule=UsingStatement term=abstract e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:abstract reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:async rank=1 pos=229 cost=1 rule=UsingStatement term=async e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:async reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:bool rank=1 pos=229 cost=1 rule=UsingStatement term=bool e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:bool reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:byte rank=1 pos=229 cost=1 rule=UsingStatement term=byte e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:byte reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:char rank=1 pos=229 cost=1 rule=UsingStatement term=char e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:char reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:decimal rank=1 pos=229 cost=1 rule=UsingStatement term=decimal e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:decimal reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:double rank=1 pos=229 cost=1 rule=UsingStatement term=double e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:double reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:dynamic rank=1 pos=229 cost=1 rule=UsingStatement term=dynamic e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:dynamic reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:false rank=1 pos=229 cost=1 rule=UsingStatement term=false e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:false reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:float rank=1 pos=229 cost=1 rule=UsingStatement term=float e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:float reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:int rank=1 pos=229 cost=1 rule=UsingStatement term=int e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:int reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:internal rank=1 pos=229 cost=1 rule=UsingStatement term=internal e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:internal reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:long rank=1 pos=229 cost=1 rule=UsingStatement term=long e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:long reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:null rank=1 pos=229 cost=1 rule=UsingStatement term=null e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:null reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:object rank=1 pos=229 cost=1 rule=UsingStatement term=object e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:object reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:partial rank=1 pos=229 cost=1 rule=UsingStatement term=partial e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:partial reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:private rank=1 pos=229 cost=1 rule=UsingStatement term=private e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:private reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:protected rank=1 pos=229 cost=1 rule=UsingStatement term=protected e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:protected reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:public rank=1 pos=229 cost=1 rule=UsingStatement term=public e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:public reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:readonly rank=1 pos=229 cost=1 rule=UsingStatement term=readonly e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:readonly reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:sbyte rank=1 pos=229 cost=1 rule=UsingStatement term=sbyte e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:sbyte reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:sealed rank=1 pos=229 cost=1 rule=UsingStatement term=sealed e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:sealed reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:short rank=1 pos=229 cost=1 rule=UsingStatement term=short e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:short reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:static rank=1 pos=229 cost=1 rule=UsingStatement term=static e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:static reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:string rank=1 pos=229 cost=1 rule=UsingStatement term=string e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:string reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:this rank=1 pos=229 cost=1 rule=UsingStatement term=this e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:this reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:true rank=1 pos=229 cost=1 rule=UsingStatement term=true e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:true reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:uint rank=1 pos=229 cost=1 rule=UsingStatement term=uint e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:uint reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:ulong rank=1 pos=229 cost=1 rule=UsingStatement term=ulong e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:ulong reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:unsafe rank=1 pos=229 cost=1 rule=UsingStatement term=unsafe e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:unsafe reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:ushort rank=1 pos=229 cost=1 rule=UsingStatement term=ushort e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:ushort reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S1:UsingStatement:void rank=1 pos=229 cost=1 rule=UsingStatement term=void e=229
TEMP-COVERS-REJECT id=S1:UsingStatement:void reason=tier-budget rank=1 count=4 budget=4
TEMP-COVERS-TRY id=S2:UsingStatement:T1:229 rank=2 pos=229 cost=1 rule=UsingStatement term=NamespaceMember e=229
TEMP-COVERS-REPARSE id=S2:UsingStatement:T1:229 next=Failure NewPos=-1 MaxFailPos=229 e=229 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S2:UsingStatement:T1:229 reason=no-progress e2=229 <= e=229
TEMP-COVERS-TRY id=S4:UsingStatement:EOF rank=4 pos=6748449 cost=142 rule=UsingStatement term=- e=229
TEMP-COVERS-REPARSE id=S4:UsingStatement:EOF next=Failure NewPos=-1 MaxFailPos=208 e=229 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S4:UsingStatement:EOF reason=no-progress e2=229 <= e=229
TEMP-COVERS-TRY id=S6:Grammar:CloseBrace rank=6 pos=1471 cost=168 rule=Grammar term=CloseBrace e=229
TEMP-COVERS-SETMEMO key=(208,Grammar,0) Success NewPos=1471 MaxFailPos=0 node=TerminalNode(Skipped)[208..1471)
TEMP-COVERS-REPARSE id=S6:Grammar:CloseBrace next=Success NewPos=1471 MaxFailPos=0 node=TerminalNode(Skipped)[208..1471) e=229 e2=1471 ErrorPos=229
TEMP-COVERS-ACCEPT id=S6:Grammar:CloseBrace branch=progress e2=1471 > e=229
TEMP-COVERS-RECOVER iter=2 currentStartPos=208 result=Success NewPos=1471 MaxFailPos=0 node=TerminalNode(Skipped)[208..1471)
TEMP-COVERS-TRY id=S0 rank=0 pos=1471 cost=0 rule=Grammar term=- e=1471
TEMP-COVERS-REPARSE id=S0 next=Failure NewPos=-1 MaxFailPos=208 e=1471 e2=229 ErrorPos=229
TEMP-COVERS-REJECT id=S0 reason=no-progress e2=229 <= e=1471
TEMP-COVERS-S6 e=1471 parseEnd=1471 s=6748449 foundT=EOF start=1471 prefixNode=TerminalNode(Skipped)[208..1471) node=SomeNode(Skipped)[208..6748449) valueNewPos=6748449 currentStartPos=208
TEMP-COVERS-GENERATE iter=2 e=1471 parseEnd=1471 candidates=1
TEMP-COVERS-CAND id=S6:Grammar:EOF rank=6 pos=6748449 cost=951214 rule=Grammar term=EOF
TEMP-COVERS-TRY id=S6:Grammar:EOF rank=6 pos=6748449 cost=951214 rule=Grammar term=EOF e=1471
TEMP-COVERS-SETMEMO key=(208,Grammar,0) Success NewPos=6748449 MaxFailPos=0 node=SomeNode(Skipped)[208..6748449)
TEMP-COVERS-MEMOHIT-EOF key=(208,Grammar,0) Success NewPos=6748449 MaxFailPos=0 node=SomeNode(Skipped)[208..6748449)
TEMP-COVERS-REPARSE id=S6:Grammar:EOF next=Success NewPos=6748449 MaxFailPos=0 node=SomeNode(Skipped)[208..6748449) e=1471 e2=6748449 ErrorPos=229
TEMP-COVERS-ACCEPT id=S6:Grammar:EOF branch=success-at-EOF end2=6748449 inputLen=6748449
TEMP-COVERS-RECOVER-EXIT-B iter=2 endAfter=6748449 inputLen=6748449 Success NewPos=6748449 MaxFailPos=0 node=SomeNode(Skipped)[208..6748449)
TEMP-COVERS-FINISH-ENTRY Success NewPos=6748449 MaxFailPos=0 node=SomeNode(Skipped)[208..6748449) inputLen=6748449 diagCache=5
TEMP-COVERS-FINISH-EXIT success node=SomeNode(Skipped)[208..6748449) finalDiags=5
=== RESULT === elapsedMs=386 Success NewPos=6748449 MaxFailPos=0 node=SomeNode(Skipped)[208..6748449)
success=True end=6748449 inputLen=6748449
health verdict=Uncovered coversInput=False absorberBytes=1263 absorberFraction=0% recoveryNodes=1 successAtEof=True
=== (1) MEMO ===
key=(208,ReservedKeyword,0) kind=Success NewPos=229 MaxFailPos=229 nodeKind=Using nodeType=TerminalNode nodeSpan=[208..229)
key=(208,Grammar,0) kind=Success NewPos=6748449 MaxFailPos=0 nodeKind=Skipped nodeType=SomeNode nodeSpan=[208..6748449)
key=(229,ReservedKeyword,0) kind=Success NewPos=235 MaxFailPos=235 nodeKind=using nodeType=TerminalNode nodeSpan=[229..235)
=== (2) TREE ===
SomeNode(Skipped) [208..6748449)
  TerminalNode(Skipped) [208..1471) RECOVERY ABSORBER
tree nodes=2 terminalNodes=1 terminalSpanSum=1263 unionCoverage=1263 inputLen=6748449
union spans (first 50): [208..1471)
=== (3) DIAGNOSTICS ===
Kind=Skipped StartPos=208 EndPos=217 RuleName=UsingDirective Terminal=- Message=skip to resync point 229
Kind=Skipped StartPos=208 EndPos=217 RuleName=Grammar Terminal=CloseBrace Message=bottom skip to 1471
Kind=Unrecovered StartPos=229 EndPos=229 RuleName=Grammar Terminal=- Message=error at 229 not recovered (absorbed to EOF), expecting: OpenParen, CloseBrace, OpenBrace, Identifier, EmptyStatement, true, false, null, this, bool, byte, char, decimal, double, float, int, long, object, sbyte, short, string, uint, ulong, ushort, void, dynamic, Base, Quote, VerbatimString, CharLiteral, DecimalIntegerLiteral, HexIntegerLiteral, DecimalRealLiteral, New, Sizeof, Typeof, Default, Checked, Unchecked, Delegate, From, Nameof, BinaryIntegerLiteral, SeparatedDecimalIntegerLiteral, SeparatedHexIntegerLiteral, SeparatedBinaryIntegerLiteral, SeparatedDecimalRealLiteral, RawString, OpenBracket, FieldExpr, Plus, Minus, Not, Tilde, Increment, Decrement, Await, InterpolatedString, $@", @$", Ref, Throw, Xor, Range, Var, Return, Break, Continue, Goto, If, While, Do, For, Foreach, Switch, Try, Using, Lock, static, async, unsafe, Else, Case, Extern, Namespace, public, private, protected, internal, abstract, sealed, partial, Class, readonly, Struct, Interface, Enum, Record, Extension, Global, ╬╡, EOF, CloseParen
Kind=Unrecovered StartPos=1471 EndPos=1471 RuleName=Grammar Terminal=- Message=error at 1471 not recovered (absorbed to EOF)
Kind=InsufficientStack StartPos=229 EndPos=229 RuleName=- Terminal=- Message=insufficient execution stack (depth guard fired at pos 229)
diagnostics count=5
=== (4) COUNTERS ===
RecoveryPasses=3 EngineGenerateCalls=3 DegradationLevel=0
ErrorInfo=null
=== END ===
```

### Dump orientation (what to look for in the verbatim dump above)

- **Sample-parse recovery session** (ignore the first block, which is grammar construction):
  - `iter=0`: `currentStartPos=208` (208 bytes of leading trivia consumed before the start rule), `result=Failure MaxFailPos=208`. 27 candidates; all S1 insertions rejected (`no-progress`/`tier-budget`); **accepted `S2:UsingDirective:T1:229` branch=progress** (`e2=229 > e=208`).
  - `iter=1`: `result=Failure MaxFailPos=229`. 101 candidates. `TEMP-COVERS-S6 e=229 parseEnd=0 s=1471 foundT=CloseBrace start=208 prefixNode=null node=TerminalNode(Skipped)[208..1471) valueNewPos=1471` → **accepted `S6:Grammar:CloseBrace` branch=progress** (`e2=1471 > e=229`); `TEMP-COVERS-SETMEMO key=(208,Grammar,0) Success NewPos=1471 ... node=TerminalNode(Skipped)[208..1471)`.
  - `iter=2`: re-parse hits the patched memo → `result=Success NewPos=1471 node=TerminalNode(Skipped)[208..1471)`. `e=1471`. Only 1 candidate generated: `TEMP-COVERS-S6 e=1471 parseEnd=1471 s=6748449 foundT=EOF start=1471 prefixNode=TerminalNode(Skipped)[208..1471) node=SomeNode(Skipped)[208..6748449) valueNewPos=6748449` → `TEMP-COVERS-SETMEMO key=(208,Grammar,0) Success NewPos=6748449 ... node=SomeNode(Skipped)[208..6748449)` → `TEMP-COVERS-MEMOHIT-EOF` → **accepted `S6:Grammar:EOF` branch=success-at-EOF** → `TEMP-COVERS-RECOVER-EXIT-B` → `FINISH-ENTRY/FINISH-EXIT` return the same result unchanged.
- **Final tree** (item 2): exactly 2 nodes — `SomeNode(Skipped) [208..6748449)` wrapping one `TerminalNode(Skipped) [208..1471) RECOVERY ABSORBER`. `terminalSpanSum=1263`, `unionCoverage=1263`, `inputLen=6748449`.

## Hypothesis verdicts

### H1 — PARTIALLY CONFIRMED (the "somewhere else" is a second `GenerateS6` call)

- Confirmed part: the first accepted S6 (iter=1) is `Success@1471` with `1471 < input.Length`:
  `TEMP-COVERS-S6 e=229 ... s=1471 ... valueNewPos=1471` + `TEMP-COVERS-ACCEPT id=S6:Grammar:CloseBrace branch=progress e2=1471 > e=229`.
- Refuted part: the final `Success@EOF` does **not** come from outside S6. It is born in a **second** `GenerateS6` invocation (iter=2) where the stop scan yields `s = EOF`:
  `TEMP-COVERS-S6 e=1471 parseEnd=1471 s=6748449 foundT=EOF ... valueNewPos=6748449` followed by `TEMP-COVERS-SETMEMO key=(208,Grammar,0) Success NewPos=6748449` and `TEMP-COVERS-MEMOHIT-EOF`.
- Why `s=EOF`: at `e=1471` the result is a memo `Success` with no failure snapshot there (`FailureSnapshotAt(1471)` is null — visible in the dump as the Unrecovered diagnostic for 1471 having **no** `expecting:` list, the `snapshot is null` fallback in `AddUnrecoveredIfS6`, `Parser.Recovery.cs:798-803`). With `snapshot == null`, the S6 stop set degenerates to `GetTerminators(empty stack) = {EofTerminal}` (`Parser.Recovery.cs:342-343`). `FindStopWithTokenJump` skips `EofTerminal` for mid-input stops (`RecoveryEngine.cs:779`) and returns `(input.Length, EofTerminal)` only at EOF itself (`RecoveryEngine.cs:766-767`) — so the floor absorbs `[1471..EOF)` in one jump.

### H2 — PARTIALLY CONFIRMED (mechanism right, `currentStartPos = S` wrong)

- Refuted part: `currentStartPos` is **never** reassigned. Its only assignment is `Parser.cs:262` (`var currentStartPos = startPos;`), and `Recover` keeps it at 208 for the whole session — dump: `TEMP-COVERS-RECOVER iter=2 currentStartPos=208`. Each iteration re-parses from 208 and rides the patched start-rule memo.
- Confirmed part: the later S6 pass (iter=2) reaches EOF but does **not** extend the tree. The new absorber `TerminalNode("Skipped", 1471, 6748449, ...)` **is constructed** (`RecoveryEngine.cs:1104`) but never attached: the `prefixNode` from the memo is a bare `TerminalNode`, so the switch takes the `TerminalNode` arm (`RecoveryEngine.cs:1113`) which wraps **only the old prefix terminal** in a `SomeNode` with span `[208..6748449)` and silently drops the new absorber. Evidence: the S6 trace shows `node=SomeNode(Skipped)[208..6748449)` while the final tree contains only `TerminalNode(Skipped)[208..1471)`. The start-rule memo is then overwritten (`SetMemo`) with this span-lying node.

### H3 — REFUTED

- `FinishRecovery` (`Parser.Recovery.cs:909`) does not declare success by any position; it attaches/finalizes diagnostics and returns the **same** `Result` unchanged. Dump: `TEMP-COVERS-FINISH-ENTRY Success NewPos=6748449 ... node=SomeNode(Skipped)[208..6748449)` → `TEMP-COVERS-FINISH-EXIT success node=SomeNode(Skipped)[208..6748449)`.
- The `Recover` exit checks (`Parser.Recovery.cs:681/892`) and `ParseCore` (`Parser.cs:282`) test exactly `result.TryGetSuccess(out _, out end) && end == input.Length`, i.e. `Result.NewPos`. `MaxFailPos` plays no role in declaring success. `NewPos=6748449` was already carried by the memo-patched result born at `RecoveryEngine.cs:1117`.

## Root cause

**`ExtensibleParser/Recovery/RecoveryEngine.cs:1113`** (in `GenerateS6`):

```csharp
ISyntaxNode node = prefixNode switch
{
    SeqNode seq  => new SeqNode(seq.Kind, [.. seq.RawElements, absorber], currentStartPos, s),          // :1111 — absorber appended
    ListNode list => new ListNode(list.Kind, [.. list.RawElements, absorber], ..., currentStartPos, s), // :1112 — absorber appended
    TerminalNode terminal => new SomeNode(terminal.Kind, terminal, currentStartPos, s),                 // :1113 — absorber DROPPED
    _ => absorber
};
var value = Result.Success(node, s, 0);                                                                 // :1117 — NewPos = s
```

Mechanism in plain words:

1. The sample file's first construct fails at byte 208 (the depth guard even fires there — see the `InsufficientStack` diagnostic), so the start-rule memo `(208, "Grammar", 0)` holds a **Failure**. The first S6 floor (iter=1) therefore has `prefixNode == null` and patches the memo to `Success(TerminalNode(Skipped)[208..1471), @1471)` — the tree is now a **bare `TerminalNode`** absorber.
2. At iter=2 the recovery point is 1471 with **no failure snapshot**, so the S6 stop set is only `{EofTerminal}` and the scan jumps straight to EOF: `s = 6 748 449`.
3. `GenerateS6` builds the new absorber `[1471..EOF)` (line 1104) — but because the memo prefix node is a bare `TerminalNode`, the switch arm at **line 1113** wraps only the *old* absorber in `new SomeNode(terminal.Kind, terminal, currentStartPos, s)` and **never includes the new absorber** (unlike the `SeqNode`/`ListNode` arms, which append it). The wrapper's span `[208..EOF)` claims coverage that no terminal node provides.
4. Line 1117 then pairs that lying node with `NewPos = s = EOF` and the candidate patches the start-rule memo with it (`Apply: p => p.SetMemo(startRule, currentStartPos, 0, value)`, line 1126). The re-parse hits the memo (`ParseRule` memo hit, `Parser.cs:336-346`), returns `Success@EOF`, `TryCandidate` accepts via the `end2 == input.Length` branch, and `FinishRecovery` returns it verbatim.

So the divergence between `Result.NewPos == input.Length` and tree coverage is created at **`RecoveryEngine.cs:1113`** (node built there omits the absorber constructed at `:1104`), sealed by **`RecoveryEngine.cs:1117`** (`Result.Success(node, s, 0)` with `s = EOF`), and consumed unchanged by the accept path (`Parser.Recovery.cs:821-833`) and `FinishRecovery` (`Parser.Recovery.cs:909`). The final tree is `SomeNode(Skipped)[208..EOF)` containing one `TerminalNode(Skipped)[208..1471)` — 1263 covered bytes against a 6 748 449-byte input.

Secondary observations (not the root cause, but relevant to the probe's `coversInput` metric):

- Leading trivia: `ParseCore` consumes 208 bytes of trivia before the start rule (`Parser.cs:274-276`), so terminal spans can never start at 0 for this file; `ParseHealth.CoversInput` requires `spans[0].StartPos == 0` (`ParseHealth.cs:73`). Even with the absorber fixed, this file would still report `coversInput=False` unless trivia is accounted for — the dominant anomaly, however, is the missing `[1471..EOF)` coverage.
- The `InsufficientStack` diagnostic (`depth guard fired at pos 229`) shows the initial parse was depth-capped very early on this file, which is why recovery started at byte 208 instead of deep in the file.

## Restore verification

All three instrumented files were copied back byte-exact from `%TEMP%\opencode\covers-research\orig\`. SHA256 after restore (identical to the pre-instrumentation snapshots):

```
1722A7D177ADF040AD33931B8B22724B856C73BA3E6E91715FB7B21C82AE81C3  ExtensibleParser/Parser.Recovery.cs
C8E303F049E5EC0F05B48C4DC84A0487F803E026D6AB2C7E742812891C9A7004  ExtensibleParser/Recovery/RecoveryEngine.cs
1A61F41118E35E8200A2B94473F350EB61FC437CCEDD7DCB385DE87452DB452F  ExtensibleParser/Parser.cs
```

`git diff --stat` → **empty** (no output). `git status --short`:

```
?? MergeGrammarIntoRecoveryBranch-checklist.md
?? MergeGrammarIntoRecoveryBranch/
```

Nothing was committed; branch `grammar` untouched; the probe test file untouched (`[Ignore]` intact); no `.csproj` edited; Roslyn MCP server not stopped.
