# Recovery Improvement — Progress 2.1 (B1): exact-hygiene `pos == e` boundary fix

**Scope:** the exact-hygiene change in `HygieneCore` (2.1/B1) made memo hygiene precise — only
genuinely invalid entries are removed on each recovery candidate. The initial implementation used
`key.pos < e` for the invalid-Failure predicate, which excluded a `Failure` recorded exactly at the
recovery point `e`. That broke the existing test
`Test_Hygiene_Removes_Failure_At_E_Keeps_Success_Partial`
(`Tests/ParserTests/Recovery/PatchRollbackTests.cs:161`), which expects a `Failure` at `pos == e` to
be removed. This note records the final condition, the counter, the test, the one-character fix, and
the results.

## The exact-hygiene condition (final)

`HygieneCore` (`ExtensibleParser/Parser.Recovery.cs:473-486`) removes only genuinely invalid memo
entries on each candidate. The invalid-Failure predicate (final, after the one-character fix):

```csharp
var isInvalidFailure = value.ResultKind == Result.Kind.Failure && key.pos >= currentStartPos && key.pos <= e;
```

Full removal rule (`Parser.Recovery.cs:475-485`):

```csharp
foreach (var key in _memo.Keys.ToList())
{
    var isStartRuleAtCurrentStart = key.pos == currentStartPos && key.rule == startRule;
    var value = _memo[key];
    var isInvalidFailure = value.ResultKind == Result.Kind.Failure && key.pos >= currentStartPos && key.pos <= e;
    if (isStartRuleAtCurrentStart || isInvalidFailure)
    {
        RemoveMemo(key.rule, key.pos, key.precedence);
        HygieneRemovals++;
    }
}
```

- **(b)** the start-rule record at `currentStartPos` (any kind: Partial/Success<EOF) — the stale
  top-level result of the first pass; removed so the re-parse re-attempts the start-rule.
- **(f)** a `Failure` with `currentStartPos <= pos <= e` — a failure that started at/after the
  re-parse origin and reaches the recovery point; its computation depended on input around `e` that the
  re-parse changes via injection/absorber → invalid. The boundary is **inclusive** (`pos <= e`) so a
  `Failure` recorded exactly at `e` is removed (the 2.1 fix).
- `Success`/`Partial` are never removed (I2: valid prefix facts).

## The `HygieneRemovals` counter

Test hook (2.1/B1) on `Parser` (`Parser.Recovery.cs:31-33`):

```csharp
public int HygieneRemovals { get; private set; }
```

Reset at the start of `Recover` (`Parser.Recovery.cs:277`) and incremented once per removal in
`HygieneCore` (`Parser.Recovery.cs:483`). It is bounded (not proportional to file size) under exact
hygiene — the invariant the test below pins.

## The `HygieneTests` test

`Tests/ParserTests/Recovery/HygieneTests.cs` — `OneErrorAtEndOfLongFile_HygieneIsBounded`.
Grammar `Module := ZeroOrMany(Stmt); Stmt := Seq("a", ";")` (no recovery rules; trailing garbage is
recovered by the bottom layer, S3/S6). Input = 200 correct `"a;"` statements + one `"b"` error at the
end. Asserts:

- `Success@EOF` (recovery reaches `end == input.Length`).
- `parser.HygieneRemovals <= 10` — removals are bounded, not proportional to the 200 statements. The
  old (imprecise) hygiene removed the whole memo (~202 records) on each candidate → removals would be
  ~200+.

## The one-character fix

The exact-hygiene change originally used `key.pos < e`, which excluded a `Failure` at `pos == e`. The
existing test `Test_Hygiene_Removes_Failure_At_E_Keeps_Success_Partial`
(`Tests/ParserTests/Recovery/PatchRollbackTests.cs:161`) asserts that after `Apply` (S0) the
snapshot-rule `Failure` at `e` is removed while `Success`/`Partial` at `e` are kept. With `pos < e`
that `Failure` survived → test failed.

Fix (`ExtensibleParser/Parser.Recovery.cs:479`), one character:

```diff
- var isInvalidFailure = value.ResultKind == Result.Kind.Failure && key.pos >= currentStartPos && key.pos < e;
+ var isInvalidFailure = value.ResultKind == Result.Kind.Failure && key.pos >= currentStartPos && key.pos <= e;
```

`<` → `<=`. Nothing else changed: the `HygieneRemovals` counter, `HygieneTests.cs`, and all other
files are untouched.

## Test results

- `dotnet build Tests/ParserTests/ParserTests.csproj` — **0 errors**, 0 warnings.
- `dotnet test Tests/ParserTests/ParserTests.csproj` — **347 passed / 0 failed / 2 skipped** (Total 349).
  The 2 skipped are the pre-existing `[Ignore("WIP")]` tests.

## Deviations

- None. The fix is exactly the one-character `<` → `<=` on the invalid-Failure predicate in
  `HygieneCore`, as specified. No other file was modified and nothing was committed.
