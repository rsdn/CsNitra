# T3.13.3 — C# 13.0 extension members (`T.X`)

## Status
BLOCKED — the "extension members" feature IS present in this Roslyn snapshot, but it is a **C# 14** feature,
not C# 13. There is **no C# 13 "extension members" feature**. Not implemented as CS13 (that would encode a
version-incorrect rule). See "Version mismatch" below.

## Task
Extend the grammar with "extension members (C# 13.0)". Find the Roslyn syntax in `C:\RSDN\roslyn`.
Version purity: CS13 only — `CreateParser(12)` must REJECT, `CreateParser(13)` must accept.

## What I searched for (Roslyn, `C:\RSDN\roslyn\src\Compilers\CSharp\Portable`)
1. `extension member` in `Parser/` → only `DocumentationCommentParser.cs:982` (an XML-doc `cref` comment,
   NOT the declaration feature).
2. `ExtensionMember` in `Portable/` → `ExtensionMemberCrefSyntax` (the XML-doc `extension(...)Member` cref)
   plus binder lookup helpers (`GetAllExtensionMembers`, `IsNonMethodExtensionMember`, `ReduceExtensionMember`)
   — all about the existing C# 3.0 extension-METHOD machinery / crefs, NOT a new C# 13 declaration form.
3. `extension` / `IsExtension` / `IsExtensionContainerStart` in `Parser/LanguageParser.cs` → **the real
   feature**: an `extension` CONTAINER declaration parsed by `ParseMainTypeDeclaration`.
4. `CSharp13` / `LanguageVersion.CSharp13` + the `MessageID` feature-flag table → to pin the version.

## What I found — the feature exists, but as an `extension` container, and it is C# 14
The "extension members" feature is an **extension declaration** — an `extension` container, a TYPE-LEVEL
declaration that HOLDS extension properties/indexers/events/operators/fields. It is NOT a per-member `T.X`
construct (the `T.X` in the checklist is shorthand for "extension members", not a literal `Type.Member` token
form).

Syntax (from `ParseMainTypeDeclaration`, `Parser/LanguageParser.cs:1789-1928`):
```
extension <typeParams>? ( params ) where-clauses? { members }     // or: extension ( params ) ;
```
- `extension` — contextual keyword (`SyntaxKind.ExtensionKeyword`).
- NO name (line 1811-1818: `name = null`; an identifier after `extension` is `ERR_ExtensionDisallowsName`).
- optional type parameter list `<...>` (line 1824).
- REQUIRED parameter list `(...)` (line 1827-1828: `isExtension` forces
  `ParseParenthesizedParameterList(forExtensionOrUnion: true)`; `requireOneElement: true`, line 4851).
- NO base list (line 1830: `isExtension ? null : ...`).
- optional `where` constraints (line 1839-1843).
- body `{ members* }` or `;` (line 1847-1914).

Parse entry points:
- `IsExtensionContainerStart()` (`LanguageParser.cs:3396-3401`):
  `CurrentToken.ContextualKind == ExtensionKeyword && (IsFeatureEnabled(IDS_FeatureExtensions) || PeekToken(1)==LessThanToken)`.
- `ParseMemberDeclaration` → `IsExtensionContainerStart()` → `ParseMainTypeDeclaration` (`LanguageParser.cs:3261-3263`).
- `IsTypeDeclarationStart()` → `IsExtensionContainerStart()` (`LanguageParser.cs:2501-2503`).
- `ParseTypeDeclaration` → `IdentifierToken` (ContextualKind `ExtensionKeyword`) → `ParseMainTypeDeclaration`
  (`LanguageParser.cs:1780-1782`).

Example (what the feature parses):
```csharp
extension (this int x)
{
    public static bool IsEven => x % 2 == 0;      // extension property
    public static string ToHex() => x.ToString("X"); // extension method
}
```

## Version mismatch (why I did NOT implement it as CS13)
`MessageID.cs` (`RequiredVersion`):
- `IDS_FeatureExtensions` (the `extension` container) → **`LanguageVersion.CSharp14`** — it is in the
  "C# 14.0 features" block (lines 509-519, specifically line 515; declaration at line 301).
- The **C# 13.0** feature block (lines 521-531) contains NO extension-related feature:
  `StringEscapeCharacter, ImplicitIndexerInitializer, LockObject, ParamsCollections, RefUnsafeInIteratorAsync,
  RefStructInterfaces, AllowsRefStructConstraint, PartialProperties, OverloadResolutionPriority`.
- Related flags: `IDS_FeatureExtensionMethod` → C# 3 (the original extension methods — already in
  `Cs3.grammar`, T3.2.4); `IDS_FeatureExtensionIndexers` → C# 15 (line 506).

Conclusion: **there is no C# 13 "extension members" feature in this Roslyn snapshot.** The extension-members
feature that exists is a **C# 14** feature. The plan lists it under "T3.13 CS13" — a version mismatch; it
belongs under **T3.14 CS14** (`docs/CSharpParserPlan.md:56`).

Side observation (snapshot drift): `IDS_FeatureFieldKeyword` (T3.13.1's `field`, documented in
`Cs13.grammar` as a C# 13.0 feature at `MessageID.cs:290`) is ALSO in the C# 14 block in THIS snapshot
(line 510). So this Roslyn snapshot is newer than the plan's version assumptions and has re-versioned several
features. That makes the "extension members = C# 14" finding authoritative for this snapshot (not a fluke).

## Why I did not write the rule in Cs13.grammar
1. Implementing it as CS13-only would encode a factual error (a C# 14 feature labelled C# 13) and make the
   version-purity tests misleadingly pass (they would assert extension members is C# 13 when Roslyn says C# 14).
2. The task forbids modifying csproj files; there is no `Cs14.grammar`, and creating one (the correct home for
   a C# 14 feature) would require a csproj change → not possible within the task constraints.
3. The task's own "do not guess / do not make up" + version-purity emphasis points to reporting, not forcing a
   wrong version.

## The rule that WOULD implement it (for T3.14 / Cs14, if the plan is corrected)
`extension` is a type-level declaration, so it goes on the `TypeDeclaration` union (Cs1.grammar:27-32), NOT on
a member rule. Append-merge (T0.3) a new alternative:
```
TypeDeclaration =
    | ExtensionDeclaration = "extension" TypeParameterList? "(" ExtensionParameterList ")" ConstraintClause* (ClassBody | ";");
```
with `ExtensionParameterList = "(" Parameter+ ")"` (at least one — Roslyn `requireOneElement: true`), reusing
Cs1 `Parameter` (Cs1.grammar:472) and Cs1 `ClassBody` (Cs1.grammar:102). Gated CS14-only (present in
Cs14.grammar, absent at v<=13). This is the intended shape; it is NOT applied here (no Cs14.grammar exists and
csproj changes are forbidden).

## Code iterations
- None. No grammar / test / build changes made — blocked on the version mismatch before writing any rule.

## Verification
- Not applicable (no code changes made). No build/test run was required because no code was written.
- If the plan is corrected to place this under T3.14 / Cs14, the task's verification steps would then apply.

## Files changed
- `docs/CSharpParserPlan-progressT3.13.3.md` (NEW, this file) — documentation only.
- No grammar, test, or csproj files modified. No commit made.
