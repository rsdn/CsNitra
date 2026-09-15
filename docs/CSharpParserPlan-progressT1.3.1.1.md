# T1.3.1.1 — Whole-word semantics for identifier-like literals in grammar texts

Progress log (append as work proceeds).

## Analysis (2026-09-15)

- `Literal.TryMatch` (Rules.cs:69-70): pure prefix `StartsWith`, confirmed.
- `ParseTerminal` trivia skip (Parser.cs:686-689) confirmed — a `!IdentifierPart` predicate after a keyword literal would see the next token's first char, hence the fix belongs inside the terminal's `TryMatch` (raw next char, no trivia skip).
- `CsNitra.Naming.IsValidIdentifier` (Naming.cs:11-30): first char `_` or `char.IsLetter` (any Unicode letter, incl. uppercase); rest `_` or `char.IsLetterOrDigit`. This is the correct identifier rule (`[_\p{L}]\w*`-ish, broader than regex `[_\l]\w*` which would reject uppercase starts). Used as-is; consistent with WordLiteral's boundary chars (`IsLetterOrDigit || '_'`).
- `RuleGenerator.GenerateExpression` creates literals at RuleGenerator.cs:74: `LiteralAst a => new EP.Literal(a.Value, a.Kind)`.
- **Terminal identity**: `TerminalComparer` / `FollowSetCalculator` / `FirstSets` treat `Literal` by `Value`, everything else by reference. Therefore `WordLiteral` **must derive from `Literal`** to keep value-identity in the terminal cache, injection layer, and follow/first sets. `Literal` is `sealed` → unsealed (deviation from "explicitly sealed/abstract" convention, recorded). This also keeps `GrammarMergeTests.AssertLiteral`'s hard cast `((ExtensibleParser.Literal)rule).Value` working for now-WordLiteral values (`"x"`, `"a"`, `"zzz"`, …).
- Grammar-text consumers found:
  - `Cs1.grammar` (CSharp project, uncommitted): keywords are `Kw*` terminals (rejected 82-terminal workaround, still in the file); its only string literals are punctuation (`"="`, `"{"`, …). No identifier-like literals → no behavior change there.
  - Self-grammar texts in tests (`CsNitraGrammarText.GetGrammarText()`, `CsNitraTests.GetGrammarText()`): identifier-like literals `"using"`, `"precedence"`, `"left"`, `"right"` — all occur in the text only as whole words (`precedence` at line start, `using`/`left`/`right` only inside quoted StringLiterals or as whole words) → metacircular parse unaffected.
  - `GrammarMergeTests` grammar texts: `"a"`, `"b"`, `"c"`, `"x"`, `"y"`, `"zzz"` become WordLiterals; inputs parsed are exactly those whole words at end-of-input → boundary holds, tests unaffected.
  - Json/MiniC/Dot/Cpp grammars are C#-rule-based (`new Literal(...)` in C#), NOT grammar texts → untouched by design. (They retain prefix semantics; out of scope, noted.)
- Recovery is ON by default (`RECOVERY` constant via `EnableRecovery=true` default, Directory.Build.props).

## Implementation

- `ExtensibleParser/Rules.cs`:
  - `Literal` unsealed (was `sealed record`) so `WordLiteral` can derive from it.
  - New `public sealed record WordLiteral(string Value, string? Kind = null) : Literal(Value, Kind)`:
    - `TryMatch`: ordinal prefix match of `Value` at `startPos` AND raw char at `startPos + Value.Length` is not `IsLetterOrDigit`/`'_'` (end-of-input = boundary OK); else -1.
    - Overrides `ToString()` (records synthesize their own — the inherited `Literal.ToString` is shadowed otherwise): `"Value"`. `Kind` = `Kind ?? Value` via `Literal`.
- `Parsers/CsNitra/CsNitraGrammar/RuleGenerator.cs:74-76` (`GenerateExpression`):
  ```csharp
  LiteralAst a => Naming.IsValidIdentifier(a.Value)
      ? new EP.WordLiteral(a.Value, a.Kind)
      : new EP.Literal(a.Value, a.Kind),
  ```
  Rationale: in every real language a keyword is a whole word (lexers emit keywords only on whole-word match), so whole-word semantics for identifier-like literals is the correct general behavior for grammar texts.

## Tests (Tests/ParserTests/CsNitra/WordLiteralTests.cs — 14 new, all pass)

- Unit: `publicity`→-1; `public x`→6; `public;`→6; EOI `public`→6; `public1`→-1; `public_`→-1; `xpublic`@1→6 and @0→-1; `publ`/`private`→-1; unicode `publіc` + Cyrillic і after →-1, `publіc;`→6; `ToString`/`Kind`; `WordLiteral is Literal`; `Literal` prefix semantics unchanged (`usingx`→5).
- Grammar-text level via `BuildFromTexts` (`Grammar = Stmt*; Stmt = "using" Identifier ";"`): `using x;` parses to EOF; `usingx;` does NOT parse (no success, `ErrorInfo` set — not a misparse as `using x;`).

## Verification (Debug, x64, --no-incremental)

- `dotnet build Nitra.sln`: 0 errors, 0 warnings.
- `Tests/ParserTests`: 324 passed, 0 failed, 2 skipped (pre-existing `[Ignore("WIP")]` in GrammarValidationTests).
  - Metacircular all green: `ShouldParseItself`, `RuleGeneratorTests.GeneratedParserShouldParseGrammarSameAsManualParser`, all `GrammarMergeTests`, `GrammarValidationTests` (active), `NamingTests`.
- `Tests/RegexTests`: 9/9 passed. `Tests/CSharpGrammarTests`: 84/84 passed (Cs1.grammar + uncommitted CSharpParserTests stay green as-is). `Tests/WiWorkflowTests`: 1/1 passed.
- No commits made.

## Notes / deviations

1. `Literal` unsealed — deviation from "every type explicitly abstract or sealed". Required by the task ("derive from Literal if the ctor allows"); a standalone `WordLiteral : Terminal` would break value-identity in `TerminalComparer`/`FollowSetCalculator`/`FirstSets` (Literal-by-Value vs reference) and the hard `(Literal)` cast in `GrammarMergeTests.AssertLiteral`.
2. `Naming.IsValidIdentifier` used as-is: first char `_` or any Unicode letter (incl. uppercase), rest `_`/letter/digit — correct C#-identifier semantics (broader than regex `[_\l]\w*`, which rejects uppercase starts).
3. External working-tree interference: mid-task, `Tests/ParserTests/ParserTests.csproj` gained duplicate `<Compile Include>` entries (→ NETSDK1022) and two CsNitra test files got BOMs prepended — not my edits; reverted via `git checkout --`. The 3 protected uncommitted files were never touched.
4. Other grammar-text consumers: only `Cs1.grammar` (Kw* terminals + punctuation literals — no identifier-like literals, no behavior change) and the self-grammar texts in tests (whole-word occurrences only, verified green). Json/MiniC/Dot/Cpp grammars are C#-rule-based and keep prefix `Literal` semantics (out of scope; e.g. MiniC's `new Literal("return")` still prefix-matches `returnx`).
