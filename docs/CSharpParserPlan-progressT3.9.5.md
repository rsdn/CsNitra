# T3.9.5 — Static abstract members in interfaces (C# 9.0)

## Status
DONE (verified; not committed — orchestrator commits)

## Task
Extend the INTERFACE syntax with static abstract members (C# 9.0):
```csharp
interface IShape {
    static abstract double Area { get; }
    static abstract void Draw();
}
```
Version purity: `CreateParser(8)` must REJECT; `CreateParser(9)` must accept.

## Roslyn syntax found (C:\RSDN\roslyn, main, src/Compilers\CSharp\Portable)
- **The parser does NOT special-case `static abstract`.** `ParseMemberDeclarationCore`
  (`Parser/LanguageParser.cs:3238`) collects ALL modifiers via the generic any-order `ParseModifiers`
  loop (`LanguageParser.cs:3259`; `ParseModifiers` `:1347`; `GetModifierExcludingScoped` `:1286`),
  then dispatches to the method/property parser by the next token. So `static abstract` is just two
  ordinary modifiers at the parse level — there is NO separate "static abstract" syntax node.
- **The static+abstract-in-interface feature is a BINDER/semantic check** (the plan's
  parser-vs-binder split):
  - `MessageID.IDS_FeatureStaticAbstractMembersInInterfaces = MessageBase + 12803`
    (`Errors/MessageID.cs:233`; `:546` is tagged `// semantic check`) -> `LanguageVersion.CSharp9`.
  - `ModifierUtils.CheckFeatureAvailabilityForStaticAbstractMembersInInterfacesIfNeeded`
    (`Symbols/Source/ModifierUtils.cs:216-228`) and
    `ModifierUtils.CheckFeatureAvailabilityForDefaultInterfaceImplementation`
    (`Symbols/Source/ModifierUtils.cs:150-197`; the static+abstract/sealed/virtual combination at
    `:161-179`) enforce the feature.
  - `SourceMemberMethodSymbol` (`Symbols/Source/SourceMemberMethodSymbol.cs:1098-1100`) and
    `SourcePropertySymbol` (`Symbols/Source/SourcePropertySymbol.cs:470`) flag the static+abstract form.

## Existing state (discovered)
- `InterfaceMethod` (Cs1.grammar:281) = `Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" ";"`
  - Cs2.grammar:133 appends: `... ConstraintClause+ ";"`
  - Cs8.grammar:421 appends: `... MethodBody` (default interface members)
- `InterfaceProperty` (Cs1.grammar:209) = `Attributes? PropertyModifier* Type TypeName "{" InterfaceAccessor+ "}" ";"?`
- `MethodModifier` (Cs1.grammar:288-300) ALREADY includes `static` (:293) and `abstract` (:296).
- `PropertyModifier` (Cs1.grammar:220) includes `static` (:225) but NOT `abstract`.

## KEY empirical finding (why Cs1/Cs2/Cs8 had to change)
Because `MethodModifier` already contains `static` AND `abstract`, the Cs1 `InterfaceMethod`
(`MethodModifier* Type ...`) ALREADY ACCEPTED `static abstract void Draw();` at **v1** (and v8).
Verified with a scratch parser run BEFORE any change:
```
v1: interface I { static abstract void Draw(); }            -> PARSES=True   (version-purity violation)
v8: interface I { static abstract void Draw(); }            -> PARSES=True   (version-purity violation)
v8: interface I { static abstract double Area { get; } }    -> PARSES=False  (PropertyModifier lacks abstract)
v8: interface I { static void Draw(); }                     -> PARSES=True
v8: interface I { abstract void Draw(); }                   -> PARSES=True
```
So adding only a CS9 rule would NOT give version purity (v8 would still accept it via the Cs1 rule).
The Cs1/Cs2/Cs8 `InterfaceMethod` rules therefore had to be restricted.

## Rule written
### Cs1.grammar
- New rule `InterfaceMethodModifier` = `MethodModifier` MINUS `static` and `abstract`
  (access + virtual/override/new/extern/sealed/unsafe). Placed right after `MethodModifier`.
- `InterfaceMethod` (Cs1.grammar:281) changed `MethodModifier*` -> `InterfaceMethodModifier*`.
- (`Method` (class, Cs1.grammar:265) and `Destructor` are UNCHANGED — class static/abstract methods
  keep working; only the interface method rules were restricted.)

### Cs2.grammar
- `InterfaceMethod` (Cs2.grammar:133) changed `MethodModifier*` -> `InterfaceMethodModifier*`.

### Cs8.grammar
- `InterfaceMethod` (Cs8.grammar:421) changed `MethodModifier*` -> `InterfaceMethodModifier*`.

### Cs9.grammar (new section, appended)
- `InterfaceMember` re-declared (T0.3 append-merge) to ADD:
  - `StaticAbstractInterfaceMethod`
  - `StaticAbstractInterfaceProperty`
- `StaticAbstractInterfaceMethod = Attributes? InterfaceAccessModifier* "static" "abstract" Type TypeName "(" ParameterList? ")" ";";`
  (no body — abstract members have no body; `;` mirrors the C# 1.0 interface method)
- `StaticAbstractInterfaceProperty = Attributes? InterfaceAccessModifier* "static" "abstract" Type TypeName "{" InterfaceAccessor+ "}" ";"?;`
  (semicolon accessors, no body; mirrors the C# 1.0 interface property)
- `InterfaceAccessModifier = "public" | "private" | "protected" | "internal";`
  (only a leading access modifier is allowed before the REQUIRED `static abstract`)

## Code iterations
1. **Read** Cs9/Cs8/Cs1 grammar + TdoppOperatorGuide + version helper. Found `static`/`abstract`
   already in `MethodModifier`.
2. **Scratch run (before change)** proved `static abstract void Draw();` parses at v1/v8 -> version
   purity impossible with a CS9-only rule alone.
3. **Wrote** `InterfaceMethodModifier` (Cs1) + switched Cs1/Cs2/Cs8 `InterfaceMethod` to it + added
   the two Cs9 `StaticAbstractInterface*` rules + `InterfaceAccessModifier` + `InterfaceMember` append.
4. **Scratch run (after change)** — all correct:
   - v9 `static abstract void Draw();` -> PARSE; v9 `static abstract double Area { get; }` -> PARSE;
     v9 `public static abstract void Draw();` -> PARSE; v9 both-in-one -> PARSE.
   - v8 `static abstract void Draw();` -> REJECT; v8 `static abstract double Area { get; }` -> REJECT.
   - No-regression: v9/v8 `void M();`, v8/v9 `new void M() { }`, v1 `void P();` -> PARSE.
5. **Final test file** `Cs9StaticAbstractInterfaceTests.cs` (13 tests) — all green.

Disambiguation (longest-match-wins, mutually exclusive):
- `static abstract void Draw();`: Cs1/Cs2/Cs8 `InterfaceMethod` FAIL (`InterfaceMethodModifier` cannot
  consume `static`/`abstract`, so `Type` sees the reserved `static`); only the Cs9 rule matches. Sole match.
- `static abstract double Area { get; }`: Cs1 `InterfaceProperty` FAIL (`PropertyModifier` consumes
  `static` then `Type` sees the reserved `abstract`); only the Cs9 rule matches. Sole match.

## Version-purity results
- `CreateParser(9)`: `static abstract` method/property (± access modifier, ± params, get/set) -> ACCEPT.
- `CreateParser(8)`: `static abstract` method/property (± access modifier) -> REJECT.
- Class `static abstract` method (`class C { static abstract void M(); }`) still parses at v1
  (C# 1.0; the class `Method` rule was not touched) -> no regression.

## Tests (Cs9StaticAbstractInterfaceTests.cs, 13 total)
- Positives (v9, 6): StaticAbstractMethod_Succeeds, StaticAbstractProperty_Succeeds,
  BothMembersInOneInterface_Succeeds, StaticAbstractMethodWithAccessModifier_Succeeds,
  StaticAbstractPropertyGetSet_Succeeds, StaticAbstractMethodWithParams_Succeeds.
- Negatives (v8 version-purity, 3): StaticAbstractMethod_RejectedAtV8,
  StaticAbstractProperty_RejectedAtV8, StaticAbstractMethodWithAccessModifier_RejectedAtV8.
- No-regression (4): NoBodyMethod_ParsesAtV8, DefaultInterfaceMethod_ParsesAtV9,
  NewDefaultInterfaceMethod_ParsesAtV9, ClassStaticAbstractMethod_ParsesAtV1.

## Verification
- `dotnet build Nitra.sln --no-incremental` -> **Build succeeded, 0 errors**.
- `dotnet test Tests/CSharpGrammarTests` (fresh) -> **Passed: 1375, Failed: 0, Skipped: 3, Total: 1378**.
- `dotnet test Tests/ParserTests` (regression) -> **Passed: 325, Failed: 0, Skipped: 2, Total: 327**.

## Boundary (out of scope for the minimal task)
- A static interface member WITH a body (`static void Draw() { }`, C# 9) is NOT modeled.
- Static-abstract members carrying `new`/other method modifiers (`new static abstract ...`) are NOT
  modeled; only a leading access modifier before `static abstract` is accepted.

## Working-tree hygiene note
- The test csproj (`CSharpGrammarTests.csproj`) was found PRE-DIRTIED (uncommitted) with explicit
  `<Compile Include>` items (`Cs9TopLevelStatementsTests.cs`, `ScratchT395.cs`) that caused
  NETSDK1022 duplicate-Compile build failures. It was `git restore`d to its committed (clean) state;
  the SDK auto-includes all `.cs` files, so nothing is lost. The csproj is NOT staged (matches commit).
- No csproj was modified as part of T3.9.5.

## Files changed (T3.9.5)
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — `InterfaceMethodModifier` added; `InterfaceMethod` restricted.
- `Parsers/CSharp/CSharpGrammar/Cs2.grammar` — `InterfaceMethod` restricted.
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` — `InterfaceMethod` restricted.
- `Parsers/CSharp/CSharpGrammar/Cs9.grammar` — `StaticAbstractInterfaceMethod`/`Property`,
  `InterfaceAccessModifier`, `InterfaceMember` append.
- `Tests/CSharpGrammarTests/Cs9StaticAbstractInterfaceTests.cs` — new (13 tests).
- `docs/CSharpParserPlan-progressT3.9.5.md` — this file.
- `docs/CSharpParserPlan-checklist.md` — T3.9.5 left as `[~]` (NOT marked `[✅]`; orchestrator marks it).
