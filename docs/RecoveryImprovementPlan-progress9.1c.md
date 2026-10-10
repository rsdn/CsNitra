# Progress — sub-point 9.1c (Wave 9, V1): why the C#-corpus probe's green is FALSE

Task: INVESTIGATION ONLY. Answer Q1–Q4 about why, on the preprocessed `NullableReferenceTypesTests.cs`
(6.7 MB), the CsNitra C# grammar reports `Success@EOF=True` but the parse tree contains almost nothing.
Read code + one temporary debug run with TEMPORARY `Trace.WriteLine` (byte-identical restore done &
verified). NO production-code changes in the final state, NO commit. Only new repo file = this file.

**STATUS: DONE (2026-09-22 17:03 UTC).** All four questions answered with traces + dumps. Production
files restored byte-identical (verified `git status`/`git diff`). Defect list + fix-first below.

## Background (as given) + reconciliation with the ACTUAL run

Established facts (9.1/9.1b/user debug): probe is green (false); tree = 2 `OpenUsingDirective` +
`Skipped [766..988)` + `Skipped [988..6748449)`; `Success@EOF=True`; nothing parsed past 766;
9.1b counters RecoveryPasses=3 / EngineGenerateCalls=3 / Memo=133 / RecoveryNodes=2.

Reconciliation of my run with the user's description (my run is the ground truth):
- Absorber boundaries MATCH the user: `[766..988)` and `[988..6748449)`.
- The user said "two valid OpenUsingDirective (727-752, 752-766)". ACTUAL: there are **~15** real
  `OpenUsingDirective` nodes spanning `[229..766)`; the last two are `[727..752)` and `[752..766)`
  (the ones the user cited). Nothing real past 766 — the user's point stands.
- The user said "`namespace` @903". ACTUAL: `namespace` is at **845** (verified: raw file
  `IndexOf("namespace ")=845`; context `";\r\n\r\nnamespace Microsoft.CodeAnalysis.CS…"`). 845 is
  INSIDE the first absorber `[766..988)`, so it is skipped. The 903 vs 845 delta is immaterial to the
  mechanism (both are inside the skipped region); I use 845 below.
- Preprocessor: `pp.Text.Length == input.Length == 6748449`, `pp.Diagnostics.Count=0`. The file's only
  4 `#if`/`#endif` pairs are at lines 136717+ (inside test data near EOF, `#if UNDEF`/`#if DEF`, both
  undefined) — the preprocessor left the text unchanged, so I parse the raw content. Consistent with
  9.1b (zero `ppDiag`).

## Plan (as executed)

1. Progress file created.
2. Recon: `git status` (pre-existing 9.1/9.2/untracked changes noted, untouched), harness presence,
   read the probe test + `CreateParser()`, read the core recovery/grammar files.
3. Build. (Incident: full-solution build hit file locks — see Incidents. Worked around by building only
   the netstandard2.0 library projects to the central `bin\Debug\netstandard2.0`.)
4. Wrote throwaway harness `%TEMP%\opencode\probe91c` (C# console app referencing the built DLLs by
   HintPath); builds `CSharpParser` exactly like the probe's `CreateParser()` (embedded `Cs1..Cs14`
   grammar, `CSharpTerminals.Trivia()`/`GetAll()`), runs `Preprocessor.Run` on the single file with the
   5 symbols, parses `pp.Text` with "Grammar", dumps tree + `RecoveryDiagnostics` + counters + result
   kind + text regions. Baseline run captured (218 lines, no traces).
5. Added TEMPORARY `Trace.WriteLine` (recorded below), rebuilt lib + harness, re-ran (traced.log, 326
   lines).
6. Dumped embedded `Cs1..Cs14.grammar` to `%TEMP%\opencode\probe91c\grammars\` via reflection; read the
   `Grammar`/`CompilationUnit`/`NamespaceMember`/`UsingDirective`/`ExternAliasDirective` rules.
7. Answered Q1–Q4.
8. Restored both production files byte-identical (`git checkout HEAD -- …`); verified `git status --short`
   + `git diff --stat` (no production file differs from HEAD except the pre-existing ones); re-ran the
   harness on the clean DLL → identical result (behavior-neutral restore).

## Decisions

- Harness is a standalone C# app (not PowerShell reflection) so the parse tree can be walked idiomatically.
  It references the repo's `bin\Debug\netstandard2.0\*.dll` by `HintPath`, so it always uses the freshly
  built (traced) assemblies. `Parser.SetDebugInput` is intentionally NOT called (display-only; keeps
  output clean; parse result identical).
- The build-lock workaround (build only `CsPreprocessor.csproj`, which transitively builds
  `ExtensibleParser`+`CSharpGrammar`+`Regex`+`CsNitraGrammar`) avoids the locked `bin\Debug\net8.0`
  test-output DLLs entirely.

## Log (UTC)

- 2026-09-22 ~15:40 — progress file created; recon (read plan/checklist/9.1/9.1b, probe test,
  RecoveryEngine, Parser.Recovery, FollowSetCalculator, RecoveryProfile/Diagnostic, CSharpParser/
  Terminals, node/Result types).
- 2026-09-22 ~15:45 — full-solution `dotnet build` FAILED on file locks (see Incidents).
- 2026-09-22 ~15:48 — built `CsPreprocessor.csproj` (netstandard2.0) → success, all DLLs in
  `bin\Debug\netstandard2.0`.
- 2026-09-22 ~15:52 — harness written + built; baseline run: `RESULT: Success end=6748449 eof=True`;
  tree = CompilationUnit[229..6748449) w/ ~15 OpenUsingDirective + `Skipped [766..988)` +
  `Skipped [988..6748449)`; 5 diagnostics (2 Unrecovered); counters 3/3/133.
- 2026-09-22 ~15:58 — 11 TEMPORARY `Trace.WriteLine` added (5 in RecoveryEngine.cs, 6 in
  Parser.Recovery.cs; exact text below). Lib rebuilt (1 CS8602 warning from a temp line), harness
  rebuilt, re-ran → full recovery flow captured.
- 2026-09-22 ~16:05 — dumped grammars; confirmed `namespace` is a `Literal` (Cs1:17), `using static`
  absent from `UsingDirective` (Cs1:11-13), `TypeName = !ReservedKeyword Identifier` (Cs1:23),
  `CompilationUnit = GlobalStatement* NamespaceMember*` (Cs9:218).
- 2026-09-22 17:00 — `git checkout HEAD --` restored both production files; `git status`/`git diff`
  verified clean (matches pre-session state); clean lib rebuild 0 warn/0 err; harness re-run on clean
  DLL → identical result.
- 2026-09-22 17:03 — progress file finalized.

## Incidents

- **2026-09-22 ~15:45 UTC — build file-lock (NOT F6, NOT NETSDK1022, NOT BOM).** `dotnet build Nitra.sln
  -c Debug --no-incremental` failed with 46× `MSB3027/MSB3021`: could not copy
  `bin\Debug\netstandard2.0\{Regex,CsNitraGrammar,ExtensibleParser,CSharpGrammar,CsPreprocessor,
  DotParser}.dll` → `bin\Debug\net8.0\` because the target DLLs were locked by
  `Microsoft Visual Studio (37724)`, `testhost (7116)`, and several `.NET Host` processes (a prior
  test run / VS was holding them). Response: did NOT kill VS/testhost; built only
  `Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` (netstandard2.0) to the central
  `bin\Debug\netstandard2.0`, which has no net8.0 copy step → success. No repo files touched by this.
- No F6 external-process csproj injection and no BOM injection observed this session. `git status`
  matches the pre-session state throughout (see Verification).

## Temporary Trace edits (recorded; ALL removed by `git checkout HEAD --`)

`RecoveryEngine.cs` (5):
1. `Generate`, first line of body (after `{`, before `var candidates = …`):
   `System.Diagnostics.Trace.WriteLine($"[9.1c GEN] e={e} resultKind={resultKind} startRule={startRule} currentStartPos={currentStartPos} parseEnd={parseEnd} snapshot={(snapshot is null ? "NULL" : "pos=" + snapshot.Pos)} stack=[{string.Join(" | ", (snapshot?.Stack ?? Array.Empty<StackFrame>()).Select(f => f.RuleName + "[" + f.Location + "]"))}]");`
2. `GenerateS3`, after `var maxS = Math.Min(e + maxSkip, input.Length);`:
   `System.Func<Terminal, string> td91c = t => t.Kind + ":" + (t is Literal ll ? "Lit(\"" + ll.Value + "\")" : t.GetType().Name);`
   + `System.Diagnostics.Trace.WriteLine($"[9.1c S3] e={e} maxSkip={maxSkip} maxS={maxS} terminators=[{string.Join(",", terminators.Select(td91c))}] stopSet=[{string.Join(",", stopSet.Select(td91c))}]");`
3. `GenerateS3`, after the scan loop's closing `}`, before `if (foundS < 0) return;`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c S3] foundS={foundS} foundT={(foundT?.Kind ?? "null")}");`
4. `GenerateS6`, after `AddSuffixObligations(snapshot, parser, stopSet);`:
   `System.Func<Terminal, string> td91c = t => …;` (same lambda) +
   `System.Diagnostics.Trace.WriteLine($"[9.1c S6] e={e} parseEnd={parseEnd} snapshot={(snapshot is null ? "NULL" : "pos=" + snapshot.Pos)} stopSet=[{string.Join(",", stopSet.Select(td91c))}]");`
5. `GenerateS6`, before `var cost = CostCalculator.SkipCost(input, e, s);`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c S6] foundS={s} foundT={foundT.Kind}");`

`Parser.Recovery.cs` (6):
1. `Recover`, after `e = RecoveryPointOf(result, input);`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c RECOVER] iter={iter} e={e} ePrev={ePrev} resultKind={result.ResultKind}");`
2. `TryCandidate`, after `attempts.TriedIds.Add(candidate.Id);`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c TRY] cand={candidate.Id} rank={candidate.Rank} pos={candidate.Pos} e={e}");`
3. `TryCandidate`, after `var e2 = RecoveryPointOf(next, input);`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c TRY] cand={candidate.Id} e2={e2} nextKind={next.ResultKind}");`
4. `TryCandidate`, accept@EOF branch, before `return true; // полностью восстановлено`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c TRY] ACCEPT@EOF cand={candidate.Id} rank={candidate.Rank} e={e}");`
5. `TryCandidate`, accept-progress branch, before `return true; // I1: прогресс…`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c TRY] ACCEPT-progress cand={candidate.Id} rank={candidate.Rank} e={e} e2={e2}");`
6. `TryCandidate`, reject path, before `RollbackPatches(log);`:
   `System.Diagnostics.Trace.WriteLine($"[9.1c TRY] REJECT cand={candidate.Id} rank={candidate.Rank} e={e} e2={e2}");`

Harness change (in %TEMP%, not the repo): `Main()` adds `Trace.Listeners.Add(new ConsoleTraceListener());`.

## The recovery flow (verbatim trace, abridged — full in `%TEMP%\opencode\probe91c\traced.log`)

```
[RECOVER] iter=0 e=766 ePrev=-1 resultKind=Success
[TRY] cand=S0 rank=0 e=766  → e2=766  → REJECT
[GEN] e=766 resultKind=Success startRule=Grammar currentStartPos=229 parseEnd=766 snapshot=pos=766
  stack=[Grammar[Rule AltIndex=0] | CompilationUnit[Rule AltIndex=0]
         | CompilationUnit[Loop ZeroOrMany Iteration=15]
         | NamespaceMember[Rule AltIndex=1]
         | ExternAliasDirective[Rule AltIndex=0]
         | ExternAliasDirective[Seq ElementIndex=0]]
[S3] e=766 maxSkip=1000 maxS=1766  terminators=[Using,Extern,Namespace,OpenBracket,public,…,Global,CloseBrace,EOF]
     stopSet=[…24 terminators… ,Alias,Identifier,Semicolon]        → foundS=767 foundT=Identifier
[S6] e=766 parseEnd=766 snapshot=pos=766 stopSet=[First(GlobalStatement) ∪ First(NamespaceMember) ∪
     terminators ∪ suffix]  (~90 terminals, incl. Namespace AND Identifier)     → foundS=767 foundT=Identifier
[TRY] S1:ExternAliasDirective:{Class,CloseBrace,Delegate,Enum,…,unsafe} (≈24) → all e2=766 REJECT
[TRY] cand=S2:ExternAliasDirective:T1:987 rank=2 e=766 → e2=987 → ACCEPT-progress   (absorber [766..987))

[RECOVER] iter=1 e=987 ePrev=766
[TRY] S0 → e2=987 REJECT
[GEN] e=987 … parseEnd=766 snapshot=pos=987
  stack=[Grammar[Rule0]|CompilationUnit[Rule0]|CompilationUnit[Loop #15]|NamespaceMember[Rule1]|
         ExternAliasDirective[Rule0]|ExternAliasDirective[Seq ElementIndex=1]]
[S3] e=987 maxSkip=1000 maxS=1987 stopSet=[…24…,Alias,Identifier,Semicolon]  → foundS=988 foundT=Identifier
[S6] e=987 parseEnd=766 snapshot=pos=987 stopSet=[huge]  → foundS=988 foundT=Identifier
[TRY] S1×many → e2=987 REJECT ; S2:…:987 → e2=987 REJECT ; S3:…:Identifier → e2=987 REJECT ;
     S4:…:EOF → e2=987 REJECT ; S5 → (SKIPPED: S3S6 tier budget 2/2 exhausted by S3+S4) ;
     S6:Grammar:Identifier rank=6 → e2=988 → ACCEPT-progress   (absorber [766..988); Unrecovered@987)

[RECOVER] iter=2 e=988 ePrev=987
[TRY] S0 → e2=987 REJECT
[GEN] e=988 … parseEnd=988 snapshot=NULL stack=[]
[S6] e=988 parseEnd=988 snapshot=NULL stopSet=[EOF:EofTerminal]   → foundS=6748449 foundT=EOF
[TRY] S6:Grammar:EOF rank=6 → e2=6748449 → ACCEPT@EOF   (absorber [988..6748449); Unrecovered@988)
```

## Q1 — S6 stop set at e=766

**Facts (trace `[9.1c S6] e=766` + `[9.1c GEN]`):**
- ACTUAL snapshot stack at e=766 (6 frames, deepest = the failed `using static` attempt):
  `Grammar[Rule Alt0] → CompilationUnit[Rule Alt0] → CompilationUnit[Loop ZeroOrMany #15] →
  NamespaceMember[Rule Alt1] → ExternAliasDirective[Rule Alt0] → ExternAliasDirective[Seq Elem0]`.
  The parser's last-mismatch frame is `ExternAliasDirective[Seq Elem0]` (expects `extern`, input is
  `using`); `NamespaceMember` is on **AltIndex 1** (= `ExternAliasDirective`, the 2nd alternative).
- S6 stop set = **anchor-First ∪ terminators ∪ R2-suffix**. Components & source per terminal:
  - **anchor-First (Loop frame `CompilationUnit[ZeroOrMany]` → `DeriveLoopAnchors` → the loop bodies'
    Refs).** Because `CompilationUnit = GlobalStatement* NamespaceMember*` (Cs9.grammar:218), there are
    TWO loop bodies → anchors **`GlobalStatement`** and **`NamespaceMember`**. So anchor-First =
    `First(GlobalStatement) ∪ First(NamespaceMember)`:
    - `First(NamespaceMember)` = {Using, Extern, **Namespace**, Global, OpenBracket, public, private,
      protected, internal, abstract, sealed, unsafe, partial, static, Class, ref, readonly, Struct,
      Interface, Enum, Delegate, Record, Extension, …} (the namespace-member keywords).
    - `First(GlobalStatement)` = the other ~70 terminals in the dump (true, false, null, this, bool…
      dynamic, Base, Quote, VerbatimString, CharLiteral, …, New, Var, Return, Break, If, While, Do,
      For, Foreach, Switch, Try, Lock, async, Plus, Minus, Not, …, Identifier, …). **This is why the S6
      set is ~90 terminals, not ~27.**
  - **terminators (`parser.GetTerminators` → `GetTerminatorsPerSite`).** Every frame below the top is a
    Rule/Loop frame (the Seq frame is the TOP, never a "parent"), so `TailOf` falls to the **rule-level
    follow fallback** (`SafeFollow`) for all of them; the dominant contributor is `Follow(NamespaceMember)`
    ≈ First(NamespaceMember) ∪ {CloseBrace} (the loop re-iteration) — hence terminators =
    {Using, Extern, Namespace, OpenBracket, public…Global, CloseBrace, EOF}.
  - **R2 suffix obligations (`AddSuffixObligations` → top frame `ExternAliasDirective[Seq Elem0]` →
    First of `extern`'s following elements `[alias, TypeName, ";"]`).** Adds **{Alias, Identifier,
    Semicolon}**. `Identifier` here is `First(TypeName)` = `First(Identifier)` (Cs1.grammar:23
    `TypeName = !ReservedKeyword Identifier`), i.e. the **alias-name** Identifier of the ExternAliasDirective.

**Why `namespace` — answer:** `namespace` (Kind `Namespace`, a `Literal`, Cs1.grammar:17
`"namespace"`) **IS in the S6 stop set** (3rd element: Using, Extern, **Namespace**, …). The premise
"namespace is NOT in the set" is **REFUTED**. It is contributed by anchor-First (`First(NamespaceMember)`)
and also present in the terminators. It is not used as a resync point for a different reason (below).

**Identifier hypothesis — CONFIRMED (with a correction).** `Identifier` (catch-all regex
`[_\l]\w*`, CSharpTerminals.cs:8) **IS in the stop set**, and it enters via a **suffix obligation**
(R2) — but specifically the **`ExternAliasDirective`'s alias-name `TypeName`**, not a `Type`/`TypeName`
of the class. The scan stopped on Identifier, but at **767** (mid-`using`, matching "sing"), not at 988:
- `S6@e=766` scan starts at 767 → Identifier matches at 767 → **foundS=767**.
- The tree's `[766..988)` absorber is actually produced by **`S6@e=987`** (a different iteration): its
  scan starts at **988** (which is AFTER `namespace`@845), so `namespace` is already inside the skipped
  region `[766..988)`; Identifier matches at 988 (the class name `NullableReferenceTypesTests`, whose
  `N` is at 987, `u` at 988) → **foundS=988**. So Identifier (class name) is what ended the absorber at
  988 — CONFIRMED — but the reason `namespace` was not the stop point is that (a) at e=766 Identifier
  matched earlier (767) and (b) at e=987 the scan started after namespace.

**Conclusion / hypothesis status:** Premise "namespace not in set" = **REFUTED** (it is in the set).
"Identifier is in the set via a suffix obligation and stops the scan before the class name" =
**CONFIRMED** (source = ExternAliasDirective alias-name TypeName, not the class Type). The scan never
resyncs at `namespace`@845 because the first recovery point is e=766 and the absorber from it extends
past 845.

## Q2 — S3 stop set / candidate ending at `namespace`?

**Facts (trace `[9.1c S3] e=766`):** stop set = terminators (24) ∪ R2-suffix {Alias, Identifier,
Semicolon}. `maxSkip=1000`, `maxS=1766` (so the window `[767..1766]` covers `namespace`@845 and the
class name@988). **`foundS=767 foundT=Identifier`** — the S3 scan stopped at **767**, NOT at 845.

**Answer:** `GenerateS3` did **NOT** emit a candidate ending at `namespace` (845). It emitted a
**1-byte absorber `[766..767)`** (rank 3, `Pos=767`). Reason: the S3 stop set contains the
`Identifier` catch-all regex (R2 suffix obligation, same source as Q1), and `Identifier` matches at
**767** (the `s` of "using", since `[_\l]\w*` matches "sing") — a position strictly before 845. The
scan breaks at the first stop-set match, so it never reaches 845. (Not a bracket-counter or MaxSkip
limit — 767 < 1766 and no enclosing-bracket depth was involved.)

Additionally, this S3 candidate was **never accepted**: candidates are tried by rank, and
**S2 (rank 2, resync 987)** was tried before S3 (rank 3) and accepted (progress e2=987). So even a
valid S3-@845 candidate would not have won here.

**Conclusion:** No S3 candidate ending at `namespace`; the S3 scan stopped at foundS=767 on the
Identifier catch-all (mid-token), a weak resync.

## Q3 — `Unrecovered` in `RecoveryDiagnostics` / acceptance branches

**Facts — full public diagnostic list (5):**
```
[Skipped]     [766..771)  term=            rule=ExternAliasDirective  "skip to resync point 987"
[Skipped]     [766..771)  term=Identifier  rule=Grammar               "bottom skip to 988"
[Skipped]     [988..1014) term=EOF         rule=Grammar               "bottom skip to 6748449"
[Unrecovered] [987..987)  term=            rule=Grammar  "error at 987 not recovered (absorbed to EOF),
                     expecting: Alias, Using, Extern, Namespace, OpenBracket, public, private, protected,
                     internal, abstract, sealed, unsafe, partial, static, Class, ref, readonly, Struct,
                     Interface, Enum, Delegate, Record, Extension, Global, CloseBrace, EOF, Identifier, Semicolon"
[Unrecovered] [988..988)  term=            rule=Grammar  "error at 988 not recovered (absorbed to EOF)"
```
**There ARE 2 `Unrecovered`.** The suspected "contract violation (no Unrecovered)" is **REFUTED** —
the two huge absorbers are each accompanied by an `Unrecovered`.

**Acceptance branches (ranks) per absorber:**
- **Absorber `[766..988)`** (final tree node) = created by **S6 @ iter 1 (rank 6)** (`S6:Grammar:
  Identifier`, parseEnd=766 → start=766, foundS=988), accepted via the **`e2 > e` progress branch**
  (e2=988 > e=987). Rank 6 → `AddUnrecoveredIfS6` fires → **`Unrecovered@987`** (with the full expected
  set, snapshot present). It ALSO carries the **S2 @ iter 0** diagnostic ("skip to resync point 987")
  attached by containment (both span `[766..771)` and both satisfy `node.StartPos<=d.Start && d.End<=
  node.EndPos`) — hence two `[Skipped]` diagnostics on one node.
  - The **S2 @ iter 0** candidate (rank 2, resync 987) was itself accepted (progress e2=987) but its
    absorber `[766..987)` was **superseded** by the iter-1 S6 absorber `[766..988)`. Rank 2 does NOT
    emit Unrecovered (gate `candidate.Rank != 6 → return`, Parser.Recovery.cs:759) — so iter 0 emitted none.
- **Absorber `[988..6748449)`** = created by **S6 @ iter 2 (rank 6)** (`S6:Grammar:EOF`, foundS=EOF),
  accepted via the **Success@EOF branch** (e2=6748449). Rank 6 → `AddUnrecoveredIfS6` fires →
  **`Unrecovered@988`** (no expected list, because snapshot==null → fallback message).
- `Recover` exit: iter 2's S6 acceptance made `next` a Success@EOF, so the loop returned via
  `FinishRecovery` at the post-candidate check (Parser.Recovery.cs:842), not via the A8/`e<=ePrev` paths.

**Conclusion:** No contract violation — both absorbers yield `Unrecovered` (both from rank-6 S6
acceptances). The result IS distinguishable from "parsed normally" (2 Unrecovered + 3 Skipped). The
**false-green persists only because the probe does not read `RecoveryDiagnostics`** (see Defect #4).

## Q4 — second absorber `[988..6748449)`: why S6 ran 6.7 MB without stopping

**Facts (trace `[9.1c S6] e=988`):** `snapshot=NULL stopSet=[EOF:EofTerminal]` → `foundS=6748449
foundT=EOF`. The second-iteration stop set is **{EOF} only** (no real terminals).

**Why:** At e=988 the snapshot is **null**. `GenerateS6` (RecoveryEngine.cs:943):
- `terminators = parser.GetTerminators(snapshot?.Stack ?? Array.Empty<StackFrame>())` → empty stack →
  `GetTerminatorsPerSite` returns **`[EofTerminal]`** (FollowSetCalculator.cs:236-237).
- anchor-First: the `if (snapshot is not null)` block is skipped → nothing added.
- R2 `AddSuffixObligations`: `if (snapshot is null) return;` → nothing added.
- ⇒ **stopSet = {EOF}**. The scan (RecoveryEngine.cs:972) checks each stop terminal at every position
  from e+1=989 to EOF; only EOF matches (at 6748449). ⇒ absorber `[988..6748449)`.

**Why snapshot==null at e=988:** iter-2's re-parse reads the **memoized** `Grammar@229` (the iter-1 S6
patched it to `Success@988`, RecoveryEngine.cs:1011/1032), so it does NOT re-descend into the class
name → no `OnMismatch` at 988 → `_lastSnapshot` remains the **iter-1 snapshot at 987**.
`FailureSnapshotAt(988)` requires `_lastSnapshot.Pos == 988` (Parser.Recovery.cs:1160) but it is 987 →
returns **null**. So the S6 floor at 988 has a **degenerate stop set** and absorbs to EOF.

**Conclusion:** The 6.7 MB absorber happens because the second recovery point (988) has **no snapshot**
(reached via memo, not a fresh descent), so the S6 floor's stop set collapses to `{EOF}`. The stop set
is NOT non-empty-with-a-real-terminal; it is exactly {EOF}. This is the systemic "absorb-to-EOF" path.

## Defect list (concrete)

1. **Grammar: `using static` (C# 6) is absent from the non-global `UsingDirective`.**
   `Parsers/CSharp/CSharpGrammar/…/Cs1.grammar:11-13` defines only `AliasUsingDirective` and
   `OpenUsingDirective`; there is no `using static <name>;` form. (Cs10.grammar:105-108 DOES model
   `global using static`, so the omission is specific to the non-global directive.)
   - Evidence: the parse dies at e=766 exactly on the `using static Microsoft.…` line; the failing
     frame is `ExternAliasDirective[Seq Elem0]` (no NamespaceMember alternative matches `using static`).
   - This is the **direct trigger** of this file's failure; without it, 100% of the file past 766 is
     unparseable regardless of recovery quality.

2. **`Identifier` catch-all in the S3/S6 stop set (via R2 suffix obligation) yields a weak, mid-token resync.**
   `Identifier` = `[_\l]\w*` (CSharpTerminals.cs:8) matches inside words (e.g. "sing" at 767 inside
   "using"). It enters the stop set through the R2 suffix obligation of `ExternAliasDirective[Seq 0]`
   (`First(TypeName)` = Identifier, Cs1.grammar:23). So S3/S6 stop at 767 (mid-token) instead of the
   valid `namespace`@845.
   - Evidence: `[9.1c S3] e=766 … foundS=767 foundT=Identifier`; `[9.1c S6] e=766 … foundS=767
     foundT=Identifier`.
   - Impact: the absorber `[766..988)` skips the valid `namespace` resync, so the whole
     namespace+class (6.7 MB) is never parsed.

3. **S6 floor degenerates to {EOF}-only when the recovery point has no snapshot (reached via memo).**
   `GenerateS6` (RecoveryEngine.cs:943-967) + `GetTerminatorsPerSite` (FollowSetCalculator.cs:236-237):
   with `snapshot==null` the stop set is exactly `{EOF}` (no anchors, no suffix). At e=988 the re-parse
   hit the memoized start rule → no snapshot → S6 absorbed 6.7 MB to EOF with no resync.
   - Evidence: `[9.1c S6] e=988 snapshot=NULL stopSet=[EOF:EofTerminal] foundS=6748449`.
   - Impact: systemic — ANY file whose second+ recovery point is reached via memo gets a giant
     absorb-to-EOF tail. This is the core of the "green but nothing parsed" behavior.

4. **The probe does not surface `Unrecovered` / absorber coverage — the direct cause of the false-green.**
   `Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs:81-86` reports only `SuccessAtEof`,
   `RecoveryPasses`, `EngineGenerateCalls`, `Memo`, `RecoveryNodes` — it never inspects
   `parser.Parser.RecoveryDiagnostics` (no `Unrecovered` count, no absorber byte-coverage). So even
   though 2 `Unrecovered` + 3 `Skipped` are present, the probe reports green.
   - Evidence: the run produces 5 diagnostics (2 Unrecovered) yet the probe's contract is
     `SuccessAtEof=True` only.

5. **`First(GlobalStatement)` pollutes the S6 stop set (broad, unfocused resync).**
   `CompilationUnit = GlobalStatement* NamespaceMember*` (Cs9.grammar:218) makes `DeriveLoopAnchors`
   yield the `GlobalStatement` anchor, so the S6 stop set includes the whole `First(GlobalStatement)`
   (~70 statement/expr/type terminals). (Not a correctness bug, but it makes the floor's stop set
   much broader than the namespace-member resync points that matter here.)
   - Evidence: `[9.1c S6] e=766 stopSet=[…true,false,null,this,bool,…,If,While,…,Var,Return,…]`.

6. **(Minor) Double `Skipped` diagnostic on one absorber node.**
   The `[766..988)` node carries both the S2@iter0 "skip to resync point 987" and the S6@iter1
   "bottom skip to 988" diagnostics (both match by containment, Parser.Recovery.cs:1002-1008).
   Diagnostic-attachment imprecision; cosmetic for the IDE.

## Fix-first recommendation

**Fix #4 first (make the probe honest), then #1 (grammar) for IDE usefulness, with #3 as the systemic
engine fix.**

- **#4 (probe reports `Unrecovered` + absorber byte-coverage) — do first.** It is the *direct, zero-risk*
  cause of the false-green (probe-only change, no parser/grammar risk). It converts the probe from
  "green ⇔ SuccessAtEof" to "green ⇔ SuccessAtEof ∧ no `Unrecovered` ∧ absorbers cover ≤ N bytes", which
  is the metric the plan actually wants ("100% to EOF"). It is also a **prerequisite for measuring any
  real fix**: without it, #1/#3 cannot be shown to help (the probe would still report green).
- **#1 (add `using static` to `UsingDirective`) — first REAL fix for IDE usefulness.** It removes this
  file's trigger (e=766); without it nothing past 766 parses no matter how good recovery is. Small,
  well-scoped (mirror Cs10.grammar:107 `GlobalStaticUsingDirective`), and immediately lets the parser
  reach the `namespace`/class body. (Other corpus files have other triggers, so #1 alone won't make the
  whole corpus clean — but it is the highest-leverage single change for THIS file.)
- **#3 (S6 floor with a null snapshot) — the top SYSTEMIC engine fix.** Make the floor compute a real
  stop set when `snapshot==null` (e.g. from the start rule's First: `Grammar→CompilationUnit→
  First(NamespaceMember)∪First(GlobalStatement)`, or synthesize a start-rule frame) instead of `{EOF}`.
  This stops the "absorb 6.7 MB to EOF" behavior on ANY file with an early error — the largest IDE
  win across the corpus. (#2 — excluding the `Identifier` catch-all from the resync stop set, or
  requiring the resync position to be a token boundary — is the natural companion to #3.)

Net: #4 makes the measurement honest now; #1 unblocks this file; #3 (+#2) fixes the class of failures.

## Files created / modified

- **Created (repo):** `docs/RecoveryImprovementPlan-progress9.1c.md` (this file — the only new repo file).
- **Temporarily modified, then restored byte-identical (verified):** `ExtensibleParser/Recovery/
  RecoveryEngine.cs`, `ExtensibleParser/Parser.Recovery.cs` (11 `Trace.WriteLine` lines; removed via
  `git checkout HEAD --`; `git diff` empty for both).
- **Throwaway (outside repo, %TEMP%\opencode\probe91c):** `probe91c.csproj`, `Program.cs`, `baseline.log`,
  `traced.log`, `grammars\CSharpGrammar.Cs1..14.grammar`. (The 9.1 `probe-harness.ps1` was NOT reused —
  a fresh C# harness was needed to walk the tree.)
- **No** grammar, engine, or preprocessor changes retained; **no** commit.

## Verification

- `git status --short` (after restore) == pre-session state:
  `M Parser.cs`, `MM ParserTests.csproj`, `M A8IterationLimitTests.cs`, `AM CSharpCorpusProbeTests.cs`,
  `M R2ExpectedStopTests.cs`, `A V2IterationLimitScaleTests.cs`, `MM checklist`, `A progress9.1`,
  `A progress9.2`, `?? progress9.1b`, `?? progress9.1c`. **`RecoveryEngine.cs` and `Parser.Recovery.cs`
  are absent** (restored to HEAD).
- `git diff --stat` == pre-session: only `Parser.cs`, `ParserTests.csproj`, `CSharpCorpusProbeTests.cs`,
  `checklist` (the pre-existing 9.1/9.2 changes). The two trace-edited files show **no diff**.
- Trace residue grep (`9.1c` / `td91c`) in both production files: **empty**.
- Final clean lib rebuild (`CsPreprocessor.csproj`, Debug): **0 warnings, 0 errors**.
- Harness re-run on the clean (restored) DLL: **identical** result (Success@EOF, 2 absorbers
  `[766..988)`/`[988..6748449)`, 5 diagnostics / 2 Unrecovered, counters 3/3/133) → restore is
  behavior-neutral.
