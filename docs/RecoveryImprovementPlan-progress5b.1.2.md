# 5b.1.2 — Wire per-call-site FOLLOW into `GetTerminators` (S3 stop-set)

Status: change complete; test verification **BLOCKED** by a pre-existing uncommitted build break (see Stop-if).

## Change 1 — Delegation (`ExtensibleParser/FollowSetCalculator.cs`)

`GetTerminators` is now a one-line delegation (existing comment above kept):

```csharp
public Terminal[] GetTerminators(IReadOnlyList<StackFrame> stack) => GetTerminatorsPerSite(stack);
```

- No other method touched (`GetTerminatorsPerSite`, `TailOf`, `SafeFollow`, `ComputeFollowSets`, etc. all unchanged).
- Production build (`ExtensibleParser.csproj`): **GREEN**, no diagnostics.

## Change 2 — Tests (`Tests/ParserTests/Recovery/FollowSetTests.cs`)

Per-site semantics applied: `GetTerminators(stack)` = the follow_site chain walked from the
innermost position outward. The **innermost frame is the current position, not a terminator source**;
a `SeqFrameLocation(R,ei)` contributes the per-site parent-Seq tail (`first(elements[ei+1..])`);
a `RuleFrame` (or non-Seq production) falls back to rule-level follow. For a **RuleFrame-only**
stack `[A, B]` (B innermost) the result is `GetFollowSet(A) ∪ {EOF}` — B's own rule-level follow
is NOT added (no Seq element context refines it).

| Test | Old expectation | New expectation | Justification (why per-site is more correct) |
|---|---|---|---|
| `Test_GetTerminators_EmptyStack_ReturnsEof` | `[EOF]` | `[EOF]` (unchanged) | `n==0 → [EOF]` in both old and per-site. No change needed. |
| `Test_GetTerminators_InnerFirst_ThenOuter` | `[q, p, EOF]` | `[p, EOF]` | Per-site returns the outermost rule's follow (`follow(A)={p}`) ∪ `{EOF}`. The innermost frame B is the current position and, having no Seq context, contributes nothing. The old union added `follow(B)={q}` — a rule-level aggregation over **all** of B's call sites (terminals that follow B at *other* call sites), not the precise continuation of **this** call path. |
| `Test_GetTerminators_Dedup` | `[p, EOF]` | `[p, EOF]` (unchanged) | Result coincides: per-site only the outer frame A contributes (`follow(A)={p}`); B's follow is not added. Comment updated so it no longer claims a "dedup" that per-site does not perform here. |
| `Test_GetTerminators_EofAtEnd` | `[p, EOF]` | `[p, EOF]` (unchanged) | Result coincides: per-site uses `follow(A)`; `follow(B)` (start symbol, `{EOF}`) is not used since B is the innermost/current frame. Comment updated for accuracy. |
| `Test_GetTerminators_OptionsOverride` | `[z, EOF]` (1 frame) | `[z, EOF]` (2 frames) | Per-site reads `Options.Terminators` from the frame **in** the follow_site chain (outer frame A), not from the innermost frame (current position, whose Options are ignored). Restructured to a 2-frame stack `[A(opts=[z]), B]` so the author-terminator feature is still exercised; `Options` override `follow(A)={p}` → `[z, EOF]`, and `'p'` is still asserted absent. Not "fitting": the same feature is preserved, only the frame carrying Options moves off the innermost position. |
| `Test_GetTerminators_ListPerSite` **(NEW wiring)** | — | contains `Kind==","`, NOT `Kind=="}"` | Wires per-site into `GetTerminators` (S3 stop-set source). X is the first element of `List := X ',' X`, so the per-site continuation at that call site is the Seq-tail separator `','` — not `'}'`, which would only come from the rule-level follow of X at an unrelated `Block` call site. Traced result: `[",", EOF]`. |

## Verification

- Production build (`ExtensibleParser.csproj`): **GREEN** (no errors/warnings).
- `dotnet test Tests/ParserTests --filter "FullyQualifiedName~FollowSetTests"`: **NOT RUN** (blocked, below).
- Full `dotnet test Tests/ParserTests`: **NOT RUN** (blocked, below).
- `Test_GetTerminators_ListPerSite`: **NOT RUN** (blocked). Logically traced to `[",", EOF]` → assertion holds.

## STOP-IF / Blocker (pre-existing, not caused by this change)

The `ParserTests` project **does not build** because of a pre-existing, **uncommitted** stray line in
`Tests/ParserTests/ParserTests.csproj` (line 14):

```xml
<Compile Include="Recovery\FollowSetPerSiteTests.cs" />
```

This is a **NETSDK1022** (duplicate `Compile` item): the file is already auto-included by default
SDK globbing, so the explicit `<Compile Include>` duplicates it. The committed 5b.1.1 (6787228) does
**not** contain this line — it is an uncommitted working-tree addition (the working tree also carries
other stray changes: a BOM in `FollowSetPerSiteTests.cs`, a whitespace reformat of
`CppInteropGenerator.csproj`, and the checklist status update marking 5b.1.1 done / 5b.1.2 in-progress).

Per the task constraint **"Do NOT modify any `.csproj`"** and **"do not improvise,"** I did **not**
remove this line. The exact one-line fix to unblock verification is to **delete line 14** above (restore
the committed baseline). Once removed, the build returns to the committed green baseline and the
mandated verification (FollowSetTests filter, then full ParserTests) can be run.

## Stop-if checklist

- [x] No assertion changed without a per-site "more correct" justification (table above).
- [ ] `Test_GetTerminators_ListPerSite` passes — **PENDING** (blocked; traced to `[",", EOF]`).
- [ ] Full ParserTests green — **PENDING** (blocked).
- [x] Only `FollowSetCalculator.cs` and `FollowSetTests.cs` modified. No `.csproj` modified.

## Files modified (this change)

- `ExtensibleParser/FollowSetCalculator.cs` — delegation only.
- `Tests/ParserTests/Recovery/FollowSetTests.cs` — per-site test updates + new wiring test.

No commit made.
