# T1.1 — Research: Roslyn grammar map — progress

## Log
- [x] Started
- [x] Parser file map (SyntaxParser.cs = old TokenList base; LanguageParser.cs = main parser, 14747 lines)
- [x] Token table (Lexer.cs, SyntaxKindFacts.cs, CharacterInfo.cs, UnicodeCharacterUtilities)
- [x] CSharpVersion gating — SURPRISE: parser no longer gates by version; only MessageID feature flags for disambiguation; full table in Errors/MessageID.cs:486
- [x] Test locations — SURPRISE: tests moved to src/Compilers/CSharp/Test/Syntax (xUnit); no .stree TestFiles in this checkout
- [x] Pitfalls (preprocessor, unicode, contextual keywords, reset points, recovery)
- [x] Write docs/RoslynGrammarMap.md
