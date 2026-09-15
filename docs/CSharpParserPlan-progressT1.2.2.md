# T1.2.2 — C# Lexing Terminals — Tests — Progress

## Status: done — build 0 warnings, 41/41 tests passed (6 pre-existing + 35 new)

## Task
Write tests for `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs`
(`[TerminalMatcher] public sealed partial class CSharpTerminals`) in
`Tests/CSharpGrammarTests/CSharpTerminalsTests.cs`.

## Approach
- Repo convention check: no `[DataTestMethod]`/`[DataRow]` anywhere in `Tests/` — all three
  test projects use plain `[TestMethod]` methods. Following the task's fallback: plain methods
  + a small local helper `AssertAll(Terminal, (string Input, int Expected)[])` (plus
  `AssertMatch` with `startPos` and an `Escape` helper so failure messages show real newlines
  as `\n`).
- Test names follow `TerminalName_Scenario_Expectation`, one method per scenario group.
- `Terminal.TryMatch(input, startPos)` → -1 on failure, else chars consumed (0 allowed).
- Trivia cases assert the exact consumed length AND maximal-run behavior: a second `TryMatch`
  at the returned end position returns 0 (end-of-input or non-trivia char).

## Actual-vs-expected discrepancies (all confirmed by test run)
1. **Trivia `// c\nx` → 5** (task said "VERIFY: 6 or 7"). Line comment `// c` (4 chars) stops
   BEFORE `\n`; the whitespace pass of the SAME maximal-run loop then consumes `\n` (1 char);
   stops at `x`. Input is 6 chars, so 7 was impossible. Documented semantic: one `Trivia`
   `TryMatch` call drains a whole maximal run — line comment + its trailing newline together —
   so the "newline left for the whitespace pass" distinction is internal to the loop, not
   observable across calls.
2. **`IntegerSuffix` on `x` → 0**, not the task's -1. The pattern `[uU]?[lL]?|[lL][uU]?` can
   match empty; the generated DFA's start state is accepting, so ANY input (including `x` and
   `""`) yields a 0-char match. This matches the task's own `""`→0 expectation; -1 was
   inconsistent. Optionality for the grammar stays at the grammar level (`Optional(...)`).
3. **`CharLiteral` `'\''` (4 chars: `'`,`\`,`'`,`'`) → 4**, not the task's 5 (impossible —
   max is the 4-char input length). Body = one `\\.` escape.
4. **`CharLiteral` `'\\n'` (5 chars: `'`,`\`,`\`,`n`,`'`) → -1**, not the task's 5 (impossible
   — the body must be exactly 1 char `[^'\n\\]` or a 2-char escape `\\.`; 3 body chars fit
   neither). The 4-char input `'\n'` (`'`,`\`,`n`,`'`) → 4 (also measured in T1.2.1 sanity).
5. **`DecimalRealLiteral` `123.456` → 7**, not the task's 5 (miscount — the input is 7 chars).

## Files
- Created: `Tests/CSharpGrammarTests/CSharpTerminalsTests.cs` — 35 test methods, 34 of them
  table-driven via the local helper; covers all 13 requested areas including `startPos > 0`.

## Verify
- `dotnet build Nitra.sln --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test Tests/CSharpGrammarTests --no-build`: **41/41 passed, 0 failed, 0 skipped**.

## Deviations
- Test class is `public class` (not `sealed`) to match sibling test classes in the project,
  even though AGENTS.md says every type must be explicitly `abstract` or `sealed`.
- First test run caught two bugs in MY test inputs (extra backslashes in the C# string literals
  for `'\''` / `'\n'` / `'\\n'`, and the `123.456` miscount) — fixed, not engine issues.
