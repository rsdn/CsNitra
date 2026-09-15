# T1.2.1 — C# Lexing Terminals — Progress

## Status: done — build + tests verified, terminal behavior sanity-checked (T1.2.1 restarted after subagent infra failure)

## Task
Create C# 1.0 lexing terminals in `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs`
(`[TerminalMatcher] public sealed partial class CSharpTerminals`) with a fine-grained,
composable number-terminal split so later C# versions can ADD terminals + re-declare
grammar rules without touching existing ones.

## Design decisions

### Punctuation is NOT terminalized
- Grammar text uses string literals (`"="`, `"var"`, …) for punctuation/keywords.
- No punctuation terminals are created in T1.2.1. The parser's `BuildFromAst`/`BuildFromTexts`
  turns `"…"` string tokens in grammar text into `Literal` rules.
- Rationale: keeps the terminal set focused on the *irregular* lexemes (identifiers, numbers,
  strings, trivia) that a DFA/regex or a hand-written scanner must handle; punctuation is
  trivial exact-match.

### `#` preprocessor directives are deferred
- `#if / #define / #region / …` are NOT lexed in T1.2.1.
- Roslyn treats `#` as trivia via a nested `DirectiveParser` (see
  `docs/RoslynGrammarMap.md` §5.1). A full `#if` mini-expression language is a separate task.
- RISK: source containing `#` will not parse until that task lands.

### Number terminal split (C# 1.0 set)
Each terminal matches exactly its own piece; the GRAMMAR (T1.3) composes them.

| Terminal | Pattern | Notes |
|---|---|---|
| `DecimalIntegerLiteral` | `[0-9]+` | decimal incl. leading zeros |
| `HexIntegerLiteral` | `0[xX][0-9a-fA-F]+` | hex |
| `OctalIntegerLiteral` | `0[0-7]+` | SEE CAVEAT BELOW |
| `IntegerSuffix` | `[uU]?[lL]?|[lL][uU]?` | optional suffix, empty allowed; covers u, l, ul, lu |
| `DecimalRealLiteral` | `[0-9]+\.[0-9]*|\.[0-9]+` | mantissa that REQUIRES a dot (unambiguously real) |
| `Exponent` | `[eE][+-]?[0-9]+` | |
| `RealSuffix` | `[fFdDmM]` | single REQUIRED char (see deviation) |

Recommended T1.3 composition (longest-match PEG — see note on `RealSuffix`):
```
IntegerLiteral =
    | HexIntegerLiteral     IntegerSuffix
    | OctalIntegerLiteral   IntegerSuffix      // only if octal is opted-in (see caveat)
    | DecimalIntegerLiteral IntegerSuffix

RealLiteral =
    | DecimalRealLiteral Optional(Exponent) Optional(RealSuffix)   // 1. 1.2 .5
    | DecimalIntegerLiteral Exponent   Optional(RealSuffix)        // 1e5
    | DecimalIntegerLiteral RealSuffix                            // 1f
```
- Bare `123` → only `IntegerLiteral` matches (RealLiteral alternatives all require a dot,
  exponent, or a required real suffix). No equal-length tie.
- `123f` / `123e5` → RealLiteral wins by consuming more chars than IntegerLiteral.

#### Deviation: `RealSuffix` is `[fFdDmM]` (required single char), not the task's `[fFdDmM]?`
- The task suggested `[fFdDmM]?` (may match empty). A terminal that can match 0 chars would
  make `DecimalIntegerLiteral RealSuffix` tie with `DecimalIntegerLiteral` on bare `123`
  (both consume 3) → longest-match equal-length error.
- Optionality is expressed at the grammar level via `Optional(RealSuffix)`. A required
  single-char suffix is strictly more composable (it can be wrapped in `Optional`; the reverse
  is not possible). So the terminal is `[fFdDmM]`.

#### Caveat: `OctalIntegerLiteral`
- The task's "verified context" lists octal `0[0-7]+` as C# 1.0, and the terminal is created
  as requested (names matter — the grammar may reference it).
- HOWEVER, real C# has NO octal literals. Verified against Roslyn
  `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\Lexer.cs:844 ScanNumericLiteral` — it
  only branches to hex (`0x`) and binary (`0b`); there is no octal branch. C# removed octal
  before 1.0 (it existed only in C/C++).
- Longest-match conflict: `0755` matches BOTH `DecimalIntegerLiteral` (`0755`, 4) and
  `OctalIntegerLiteral` (`0755`, 4) → equal length → PEG error. `089` is fine (octal only
  matches `0`, decimal matches `089`).
- Recommendation for T1.3: do NOT reference `OctalIntegerLiteral` (matches real C#), or if a
  historical/C++ interop mode is ever needed, guard it with a lookahead predicate
  (`&(![89])`-style) so it cannot tie with decimal. The terminal is provided but unused by default.

### String / char / verbatim
| Terminal | Pattern | Notes |
|---|---|---|
| `StringLiteral` | `"([^"\n\\]|\\.)*"` | C# 1.0: no raw newlines in regular strings; `\.` = any escape |
| `VerbatimStringLiteral` | `@"(""|[^"])*"` | `""` doubling; DFA resolves the trailing-quote ambiguity greedily |
| `CharLiteral` | `'([^'\n\\]|\\.)'` | single char or one escape between quotes |

### `Identifier`
- `[_\l]\w*` — `\l` = Unicode letter, `\w` = letter/digit/underscore. Combining marks etc. are
  out of scope (documented risk, same as the task's note).

### `Trivia` — custom (hand-written) `Terminal`, NOT regex
- DFA cannot recurse, so nested `/* … /* … */ … */` needs a hand-written `TryMatch`.
- `CSharpTerminals.Trivia()` returns a cached `TriviaTerminal : Terminal("Trivia")` instance.
- `TryMatch` consumes ONE maximal run of consecutive trivia at `startPos`:
  whitespace (`char.IsWhiteSpace`), `//` line comments (to end of line, newline left for the
  whitespace pass), and `/* … */` block comments WITH NESTING (depth counter).
- Returns `pos - startPos` (0 when `startPos` is not trivia). Unterminated block comment
  consumes to end of input (treated as trivia).
- Matches how `CsNitraTerminals.Trivia` is consumed: the parser matches it repeatedly at each
  position; a single call already drains the whole maximal run, so subsequent calls return 0.

## Build / test
- `dotnet build Nitra.sln` (repo root): **0 warnings, 0 errors**.
- `dotnet test Tests/CSharpGrammarTests --no-build`: **6/6 passed, 0 failed**.
- `dotnet test Tests/ParserTests --no-build`: **310 passed, 0 failed, 2 skipped** (pre-existing skips: `ShouldReportErrorForUndefinedRuleReference`, `RequiredSubruleNamesAreNotSpecified`).

## Compile fixes applied during restart (on top of the already-known `and`→`&&`)
The file was left with `and` (a C# *pattern* operator) used in *boolean* contexts, plus two
further latent compile errors that only surfaced once the `and` errors were gone. All fixed
minimally, no restructuring of the terminal set/design:

1. **`and` → `&&` in boolean contexts** (the known breakage). Lines in `TriviaTerminal.TryMatch`:
   - `while (pos < length && depth > 0)`
   - `if (pos + 1 < length && input[pos] == '*' && input[pos + 1] == '/')`
   - `else if (pos + 1 < length && input[pos] == '/' && input[pos + 1] == '*')`
   - Line `while (pos < length && input[pos] is not '\n' and not '\r')` was LEFT AS-IS: here
     `and not '\r'` is a valid *pattern* (`is (not '\n') and (not '\r')`), not a boolean.
2. **`TriviaTerminal` was `private sealed class ... : Terminal("Trivia")`** → `Terminal` is an
   *abstract record* (`ExtensibleParser/Rules.cs:35`), so a plain `class` (a) cannot use the
   `: Terminal("Trivia")` positional base-argument syntax and (b) fails to implement the
   synthesized abstract `Terminal.<Clone>()` (CS8861/CS8865/CS0534). Fixed by making it a
   **record** (matches codebase convention — e.g. `RecoveryTerminal : Terminal(Kind)`) with an
   explicit constructor for the base arg:
   ```csharp
   private sealed record TriviaTerminal : Terminal
   {
       public TriviaTerminal() : base("Trivia")
       {
       }
       ...
   ```
   (A record's `base_list` does not accept an argument list without a primary constructor, so
   `: Terminal("Trivia")` was invalid even for a record — the base arg moved into the ctor.)
3. **`if (c is not '/' or pos + 1 >= length)`** → `if (c != '/' || pos + 1 >= length)`. After
   `is`, the whole RHS is a *pattern*, so `or pos + 1 >= length` was parsed as a pattern arm;
   `pos + 1 >= length` is a boolean, not a pattern (CS0266/CS9135). Intent was a plain boolean.

## Sanity check (throwaway test, run, then deleted)
Verified via a temporary `TempSanityCheck` MSTest class in `Tests/CSharpGrammarTests` (deleted
afterwards). All values below are the measured actuals:

| Terminal | Input | Expected (task) | Actual |
|---|---|---|---|
| `HexIntegerLiteral` | `0xFF` | 4 | **4** |
| `HexIntegerLiteral` | `0x` | -1 | **-1** |
| `DecimalRealLiteral` | `.5` | 2 | **2** |
| `StringLiteral` | `"a\"b"` | 6 | **6** |
| `CharLiteral` | `'\n'` (4 chars: `'`,`\`,`n`,`'`) | 4 | **4** |
| `CharLiteral` | `''` | -1 | **-1** |
| `Trivia` | `  // hi\n\t/* a /* b */ c */ x` | 24 | **27** ⚠ |
| `Identifier` | `foo` | 3 | **3** |
| `Identifier` | `1abc` | -1 | **-1** |

### ⚠ Trivia: actual 27, not the task's 24
The input is **28** characters, and "everything except trailing `x`" is **27** — so 27 is the
correct maximal-trivia-run length. The task's "24" was a rough estimate, not a measured value.
Breakdown of the 27 consumed chars: 2 (leading spaces) + 5 (`// hi`) + 1 (`\n`) + 1 (`\t`) +
17 (nested block comment `/* a /* b */ c */`) + 1 (trailing space before `x`) = 27; `x` is
correctly not consumed. Nested-comment depth tracking works as designed.
