# Progress 9 — ParseHealth.cs: coverage invariant (I4)

## Intentions

Single-file change: `ExtensibleParser/Recovery/ParseHealth.cs`.

1. Read the existing file first (small).
2. `ParseVerdict`: append `Uncovered` as the LAST member (after `Failed`); update the Russian enum XML doc — Uncovered = Success@EOF but the tree's terminal spans do not tile the input (invariant I4 violated: parse claims success without covering the input).
3. `ParseHealthReport`: add positional property `bool CoversInput` between `RecoveryNodes` and `Verdict`. No custom ToString.
4. `Analyze` (signature unchanged): compute `coversInput`:
   - Collect `(StartPos, EndPos)` spans of ALL `TerminalNode` nodes reachable from `root` (including absorbers), same traversal shape: `SeqNode.RawElements`, `ListNode.RawElements`, `ListNode.Delimiters`, `SomeNode.Value`. Null root → empty list.
   - Rules in order:
     a. `inputLength == 0` && no spans → true.
     b. no spans && `inputLength > 0` → false.
     c. else: sort by `StartPos`; first span `StartPos == 0`; every consecutive pair `next.StartPos == prev.EndPos`; last span `EndPos == inputLength`. All hold → true, else false.
   - Keep the existing absorber-bytes computation (spans may be collected in the same traversal).
5. Verdict order: `!successAtEof` → Failed; else `!coversInput` → Uncovered; else `AbsorberBytes * 2 > inputLength` → FalseGreen; else `AbsorberBytes == 0` → Clean; else Recovered.
6. Return report with `CoversInput = coversInput`.
7. Update Russian XML docs (class/method) to mention the coverage invariant (I4): терминальные спаны дерева должны покрывать вход без пропусков и наложений; Uncovered ловит Success@EOF при пустом/неполном дереве (memo-патч без реального разбора).

Style: Allman braces, 4-space indent, CRLF, UTF-8 BOM preserved, Russian XML docs matching the file.

## Status log

- [x] Progress file created.
- [x] Read ParseHealth.cs (81 lines; `TerminalNode(Kind, StartPos, EndPos, ContentLength, IsRecovery, IsAbsorber)` confirmed in `ExtensibleParser/SyntaxTree.cs`).
- [x] Applied all spec changes to `ExtensibleParser/Recovery/ParseHealth.cs` (only file touched):
  - `ParseVerdict.Uncovered` appended as last member + Russian enum doc (I4).
  - `ParseHealthReport` gained positional `bool CoversInput` between `RecoveryNodes` and `Verdict` (no custom ToString).
  - `Analyze` signature unchanged; added `coversInput` via new private `CoversInput` + `CollectTerminalSpans`
    (same traversal shape: `SeqNode.RawElements`, `ListNode.RawElements`, `ListNode.Delimiters`, `SomeNode.Value`;
    null root → empty span list; rules a/b/c exactly as specified).
  - Verdict chain replaced: Failed → Uncovered → FalseGreen → Clean → Recovered.
  - Class/method Russian XML docs updated with the I4 coverage invariant wording.
- [x] Encoding verified after edits: UTF-8 BOM preserved, 130 CRLF / 0 lone LF.
- [x] No other code constructs `ParseHealthReport` positionally (grep over repo); the only external consumers
  (`Tests/CSharpGrammarTests/CSharpCorpusHealthProbeTests.cs`, `Tests/ParserTests/Recovery/UsingStaticRecoveryTests.cs`)
  use `ParseHealth.Analyze` + `health.Verdict`/properties, and the verdict `switch` there is a statement (not an
  exhaustive expression), so the new enum member breaks nothing.

## Build result

1. Acceptance command, run once from repo root: `dotnet build Tests/ParserTests --no-incremental`
   → **6 Errors / 36 Warnings — but all are MSB3027/MSB3021/MSB3026 file-lock copy failures**, not compile errors:
   `bin/Debug/net8.0/{ExtensibleParser,Regex,DotParser}.dll` are locked by `testhost (35992)` and
   `Microsoft Visual Studio / devenv (51968)`, both still running. CoreCompile of every project succeeded
   (the build reached the copy stage for the freshly produced `netstandard2.0` DLLs).
   Per instructions this is a failure outside `ParseHealth.cs` → stopped, did not kill the user's processes.

2. Diagnostic re-run with a redirected output dir (no source changes, temp dir only):
   `dotnet build Tests/ParserTests --no-incremental -p:OutDir=%TEMP%\opencode\ph9-out\`
   → **Build succeeded. 0 Warning(s), 0 Error(s)**. Fresh `ExtensibleParser.dll` (243 200 bytes, timestamped now)
   contains `Uncovered`, `CoversInput`, `CollectTerminalSpans` — the new API compiled into the assembly.

Conclusion: `ParseHealth.cs` compiles cleanly (0 errors, 0 warnings). The acceptance command's 6 errors are
environmental (locked output DLLs held by a running testhost + Visual Studio); re-running it after those
processes release the locks should yield 0 errors.

Nothing was committed (`git status --porcelain` shows `ParseHealth.cs` still untracked).
