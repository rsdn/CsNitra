# Roslyn Grammar Map — C# 1.0 «до типов» (фаза T1.1)

Исследован репозиторий `C:\RSDN\roslyn` (ветка `main`, последний коммит `9f220ee5d10`, состояние **после C# 14** — уже есть closed classes, unions, extensions, unsafe evolution). Все ссылки ниже — `file:line` по этому чекату.

## Ключевые сюрпризы (сразу)

1. **`TokenList.cs` больше не существует.** Его роль раздвоена:
   - `src/Compilers/CSharp/Portable/Parser/SyntaxParser.cs` (1193 строк) — абстрактный базовый класс токенов (буфер, `CurrentToken`/`PeekToken`/`EatToken`, reset points, missing tokens, skipped syntax, `CheckFeatureAvailability`).
   - `src/Compilers/CSharp/Portable/Parser/LanguageParser.cs` (**14747 строк**, 684 КБ) — сам парсер, `internal sealed partial class LanguageParser : SyntaxParser` (`LanguageParser.cs:20`). Парсируются также partial-файлы `LanguageParser_InterpolatedString.cs` и `LanguageParser_Patterns.cs`.
2. **Версионная гейтация в парсере практически отсутствует.** Идиомы вида `context.LanguageVersion >= CSharpVersion.CSharpX` из старых версий Roslyn **вычищены из парсера**: в `Parser/*.cs` `LanguageVersion` встречается только в `DirectiveParser.cs:337-353` (директива `#langversion`). Парсер парсит синтаксис последней версии, а ошибки «фича недоступна в C# N» выносит **байндер** (`Compilation.IsFeatureEnabled(MessageID.IDS_FeatureX)`, напр. `Binder/Binder_Operators.cs:4832`). Полная таблица «фича → версия» — `Errors/MessageID.cs:486-746` (см. раздел 3).
3. **Тесты переехали.** В этом чекате нет папки `tests/` на корне. C# синтаксические тесты — в `src/Compilers/CSharp/Test/Syntax/` (xUnit, не MSTest), без `TestFile`/`.stree`/`.diag`/`.cspans`-файлов VerifyTest (они в upstream под `tests/Compilers/CSharp/TestData/Parser/` и здесь отсутствуют).

---

## 1. Карта файлов парсера и методы границы фазы

Каталог `src/Compilers/CSharp/Portable/Parser/`:

| Файл | Роль |
|---|---|
| `SyntaxParser.cs` | Базовый класс токенов (бывш. TokenList): буфер `_lexedTokens`, `CurrentToken:316`, `PeekToken:466`, `EatToken():486`, `TryEatToken:497`, `EatToken(kind):521` (создаёт MissingToken при несовпадении), `EatTokenAsKind:537`, `CreateMissingToken:552`, `EatTokenEvenWithIncorrectKind:609`, `EatContextualToken:645,659`, `ConvertToKeyword:1104`, `ConvertToIdentifier:1123`, `GetResetPoint:158`/`Reset:170`, `AddError:749,760`, `AddLeadingSkippedSyntax:958`/`AddTrailingSkippedSyntax:970`, `CheckFeatureAvailability:1147`, `IsFeatureEnabled:1165`, `IsMakingProgress:1178` (защита от бесконечных циклов) |
| `LanguageParser.cs` | Основной парсер (все методы ниже). Флаги-терминаторы: `TerminatorState:58`, `IsTerminator():94` |
| `Lexer.cs` (+ `Lexer_StringLiteral.cs`, `Lexer_RawStringLiteral.cs`, `Lexer.Interpolation.cs`) | Лексер (раздел 2) |
| `QuickScanner.cs` | Быстрый state-machine сканер коротких токенов (кэш ≤ 42 байта, `QuickScanner.cs:17`) |
| `LexerCache.cs` | Кэш-словарь «текст → SyntaxKind» для ключевых слов (`LexerCache.cs:174`) |
| `CharacterInfo.cs` | Классификация Unicode-символов (partial `SyntaxFacts`): пробел/перенос/идентификатор |
| `DirectiveParser.cs`, `Directives.cs` | Препроцессор `#if/...` как тривиум (раздел 5) |
| `DocumentationCommentParser.cs` | XML-комментарии (не в фазе T1.x) |
| `SlidingTextWindow.cs` | Текстовое окно |
| `Blender*.cs`, `BlendedNode.cs` | Инкрементальный парсинг (не в фазе) |
| `SyntaxFactoryContext.cs` | Контекст построения green-узлов |

### 1.1 Компиляционная единица

- **`ParseCompilationUnit()`** — `LanguageParser.cs:168`. Обёртка со stack-guard (`ParseWithStackGuard:207`, при `InsufficientExecutionStackException` — `CreateForGlobalFailure:223`, весь ввод становится одним BadToken).
- **`ParseCompilationUnitCore()`** — `LanguageParser.cs:180`. Делегирует в `ParseNamespaceBody` (parentKind = `CompilationUnit`), затем `EatToken(EndOfFileToken):189`; начальные «битые» узлы вешает ведущим тривиумом на первый токен (`192-197`). Форма узла: `CompilationUnit(Externs, Usings, AttributeLists, Members, EOF)`. Top-level statements в этом же теле (см. `ParseMemberDeclarationOrStatement:2574`) — вне фазы, но влияют на форму.

### 1.2 using / extern alias

- **`ParseNamespaceBodyWorker()`** — `LanguageParser.cs:562` (обёртка `ParseNamespaceBody:410`). Основной цикл `while(true) switch(CurrentToken.Kind)` (`584-764`):
  - `NamespaceKeyword:588` → `ParseNamespaceDeclaration`;
  - `CloseBraceToken:603` — в глобальном namespace лишний `}` пропускается с ошибкой `ERR_EOFExpected` (`610-621`) — важный приём рекавери;
  - `EndOfFileToken:629` — выход;
  - `ExternKeyword:633` — дисамбигуация через 4-токенный lookahead `ScanExternAliasDirective():905` (`extern alias X;` vs `extern`-local function / extern member);
  - `UsingKeyword:660` — дисамбигуация using-directive vs using-выражения/using-declaration (`661`);
  - `IdentifierToken` с `ContextualKind==GlobalKeyword` + `using` (`674-685`) — `global using`;
  - `OpenBracketToken:687` — глобальные атрибуты `[assembly:...]` через `IsPossibleGlobalAttributeDeclaration():1012` (lookahead `[ target :`);
  - `default:726` — `ParseMemberDeclarationOrStatement` (глобально) / `ParseMemberDeclaration`; при `null` — **рекавери: съесть один токен с ошибкой** (`732-748`).
  - Контроль порядка: `NamespaceParts seen` + `adjustStateAndReportStatementOutOfOrder:776` (externs → usings → attrs → types → members); using после элементов → `ERR_UsingAfterElements` (`835-844`), extern → `ERR_ExternAfterElements` (`645-649`).
  - Post-pass `ParseNamespaceBody:410-467`: «сбежавшие» type-only члены (методы/свойства после закрытия типа) **переносятся в предшествующее type-объявление** (`moveSiblingMembersIntoPrecedingType:496`) — рекавери от «лишнего `}`».
- **`ParseExternAliasDirective()`** — `LanguageParser.cs:918`. `extern` + контекстный `alias` (`EatContextualToken(AliasKeyword)`) + `ParseIdentifierToken` + `;`.
- **`ParseUsingDirective()`** — `LanguageParser.cs:942`. Опциональный `global` (`949-951`, контекстный), `using`, `static`/`unsafe` (`956-957`; перестановка `using unsafe static` лечится missing-`static` + skipped `960-965`), алиас `name =` (`967`, `ParseNameEquals:934`). Тип: `ParseQualifiedName()` без алиаса, `ParseType()` с алиасом (`1000`). Рекавери: если после `using` токен, способный начать namespace-member, вставляется missing identifier + missing `;` (`973-993`).
- Вспомогательные: `IsPossibleNamespaceMemberDeclaration():869`, `IsPossibleStartOfTypeDeclaration():331`, `IsTypeModifierOrTypeKeyword():337`.

### 1.3 Namespace declaration (block form)

- **`ParseNamespaceDeclaration()`** — `LanguageParser.cs:236` (обёртка `_recursionDepth++` + `StackGuard`).
- **`ParseNamespaceDeclarationCore()`** — `LanguageParser.cs:247`. `namespace` → `ParseQualifiedName():259` → ветвление:
  - `;` → **file-scoped** (`264-304`, C# 10): тело парсится через тот же `ParseNamespaceBody` (parentKind `FileScopedNamespaceDeclaration`);
  - `{` (или токен, который *может* идти после `{` — тогда missing `{`, `268-280`) → block form: тело `ParseNamespaceBody:308`, `}` (`320`), опциональный `;` (`321`).
- PEG-заметки: имя — `QualifiedName` (без `::`-алиасов валидно, но парсер принимает `::` с ошибкой — см. 1.6); выбор `;` vs `{` — чистая ordered choice по следующему токenu; «может идти после `{`» — это `IsPossibleNamespaceMemberDeclaration` (т.е. lookahead `!` по началу члена).

### 1.4 Namespace member declarations: заголовки

- **`ParseTypeDeclaration()`** — `LanguageParser.cs:1756`. Диспатч по `CurrentToken.Kind`: `ClassKeyword`/`StructKeyword`/`InterfaceKeyword` → `ParseMainTypeDeclaration`; `DelegateKeyword` → `ParseDelegateDeclaration`; `EnumKeyword` → `ParseEnumDeclaration`; `IdentifierToken` с `ContextualKind` ∈ {`Record`, `Extension`, `Union`} → `ParseMainTypeDeclaration`.
- **`ParseMainTypeDeclaration()`** — `LanguageParser.cs:1790` (class/struct/interface/record/extension/union в одном методе):
  - `tryScanRecordStart:1931` — `record` + опц. `class`/`struct`-модификатор; `struct record S` лечится как RecordStruct с `ERR_MisplacedRecord` (`1943-1958`);
  - имя `ParseIdentifierToken:1821`;
  - **generic clause** `ParseTypeParameterList:1824`;
  - parameter list только для extension/union (`1827-1828`);
  - **base list** `ParseBaseList:1830`;
  - constraint clauses `where` (`1839-1843`, `ParseTypeParameterConstraintClauses:2242`);
  - `;` (extension) vs `{ ... }` (`1850-1858`);
  - цикл членов: `CanStartMember:1876` / `IsTerminator:1896` / `SkipBadMemberListTokens:1891,1904`;
  - `constructTypeDeclaration:1965` — построение узла по keyword.
- **`ParseBaseList()`** — `LanguageParser.cs:2170`. Опц. `:` (`2175`), первый тип `ParseType:2182`, primary-constructor-base `Type(args)` (`2183-2185`, C# 12), цикл `, type` (`2188-2196`), рекавери «забытая запятая» (`2198-2220`). Терминаторы: `{`, `;`, `where T:` (`IsCurrentTokenWhereOfConstraintClause:2234` — lookahead `where <id> :`).
- **`ParseTypeParameterList()`** — `LanguageParser.cs:6104`. `<` → список `ParseTypeParameter` (`6115-6123`) через общий `ParseCommaSeparatedSyntaxList:14490,14512`; `IsStartOfTypeParameter:6142` (атрибуты `[`, variance `in`/`out`, идентификатор); `ParseTypeParameter:6155` (атрибуты + variance + имя).
- **`ParseTypeParameterConstraintClauses():2242`**, `ParseTypeParameterConstraintClause():2250` (`where` + имя + `:` + bounds), `ParseTypeParameterConstraint():2346` (type | interface list | `struct`/`class`/`unmanaged`/`enum<T>`/`delegate`/`new()`/`notnull`).
- **`ParseDelegateDeclaration()`** — `LanguageParser.cs:5835`. `delegate` + `ParseReturnType:3738` + имя + generic clause + `ParseParenthesizedParameterList` + constraints + `;`. (Parameter list — вне заявленной границы фазы «до типов», но нужен для делегата как термина заголовка.)
- **`ParseEnumDeclaration()`** — `LanguageParser.cs:5868`. `enum` + имя; generic-параметры enum **запрещены** — вешаются skipped-тривиумом с `ERR_UnexpectedGenericName` (`5878-5882`); опц. базовый тип — ровно один, без запятых (`5885-5894`); `;` vs `{ members }` (`5901-5929`); `ParseEnumMemberDeclaration:5952` (атрибуты, имя, `= <const-expr или identifier>`), запятые/точки-с-запятой как separator (`allowSemicolonAsSeparator: true`, `5924`).
- **`ParseModifiers()`** — `LanguageParser.cs:1347`. Цикл по `GetModifierExcludingScoped:1283,1286` (public/internal/private/protected/sealed/abstract/static/virtual/extern/new/override/readonly/volatile/unsafe/partial(async)/ref/contextual). Ключевые дисамбигуации:
  - `partial` — только если `IsCurrentTokenDefinitelyPartialModifier:1637` (lookahead: после `partial` идёт старт type/member declaration);
  - `async` — `ShouldContextualKeywordBeTreatedAsModifier:1501` (следующий токен — немодификаторный modifier, или после проглатывания виден partial/старт объявления);
  - `ref` — `isRefReturningMember:1448` (speculative scan «ref-тип + имя члена» → `ref` оставляет для return type) и `shouldConsumeRefAtTopLevel:1465`;
  - C#13+ contextual-модификаторы `file`/`closed`/`required`/`safe` — гейтятся `IsFeatureEnabled` **только для дисамбигуации** (`1411-1437`);
  - `scoped` (`1358`).
- **Атрибуты**: `IsPossibleAttributeDeclaration():1031` (reset-point: `[` + (identifier | `word:` | не-literal)), `ParseAttributeDeclarations():1059` (защита от вложенных атрибутов в аргументах, `1076-1077`), `TryParseAttributeDeclaration():1103` (дисамбигуация attribute vs collection expression: `[A, B].`/`[A, B]->`/`[A, B]?.` → сброс и re-parse как коллекция, `1133-1164`), `ParseAttribute():1181` (`ParseQualifiedName` + `ParseAttributeArgumentList:1193`), `ParseAttributeArgument():1253` (`name =` / `name:` по lookahead `PeekToken(1)`).
- **`ParseMemberDeclaration()`** — `LanguageParser.cs:3223` (обёртка) / `ParseMemberDeclarationCore:3238`: attributes (`3256`) → modifiers (`3259`) → extension-container (`3261`, `IsExtensionContainerStart:3396`) → constructor `Ident(` (`3267`) → destructor `~` (`3273`) → const/event/fixed/conversion (`3279-3301`) → **type declaration** `isPossibleTypeDeclaration && IsTypeDeclarationStart():3306` (`IsTypeDeclarationStart:2483`) → return type + name + method/field/property/indexer (`3314-3382`). Для фазы T1.x важен только путь до type declarations.
- `CanStartMember():2427` — набор токенов, начинающих член (используется в циклах членов и терминаторах).

### 1.5 Типы

- **`ParseType()`** — `LanguageParser.cs:7566`. Префикс `ref`/`ref readonly` → `RefType` (`7568-7574`), иначе `ParseTypeCore:7579`.
- **`ParseTypeCore()`** — `LanguageParser.cs:7579`. `ParseUnderlyingType:7610`, затем postfix-цикл (`7614-7676`, guard `IsMakingProgress`):
  - `?` → nullable: `TryEatNullableQualifierIfApplicable:7682` — крупная дисамбигуация «nullable type vs условное выражение/паттерн» в контекстах `is`/`as` (lookahead до `:` и т.п., `7706-7858`);
  - `*` → pointer: `ParsePointerTypeMods:8173` (цикл `*`), в паттерн-контекстах — только если дальше идёт ранг массива (`7630-7655`);
  - `[` → array: цикл `ParseArrayRankSpecifier:7860` (`7656-7668`) — **multi-dim** `[,,]` через `OmittedArraySizeExpression`, смешение omitted/non-omitted размеров лечится (`7897-7912`).
- **`ParseUnderlyingType()`** — `LanguageParser.cs:7969`.
  - predefined: `IsPredefinedType:7531` → `SyntaxFacts.IsPredefinedType` (`Syntax/SyntaxKindFacts.cs:316`): 17 типов — `bool byte sbyte int uint short ushort long ulong float double decimal string char object void`; `void` допускается с ошибкой (`7975-7978`);
  - identifier (или `::` для рекавери) → `ParseQualifiedName:7986`;
  - `(` → tuple type `ParseTupleType:7920` (C# 7);
  - `delegate *` → function pointer (`7993`, C# 9, `ParseFunctionPointerTypeSyntax:8004`);
  - иначе — missing identifier + `ERR_TypeExpected` (`7998-8000`).
- **Имена**: `ParseQualifiedName():7031` = `ParseAliasQualifiedName:7023` + цикл `.`/`..` (`7036-7045`); `ParseQualifiedNameRight:7050` — `::` валиден только `Ident::Name`, иначе превращается в `.` с `ERR_UnexpectedAliasedName` (`7063-7088`).
- **Generic-имена**: `ParseSimpleName():6182` — после identifier при `<` запускается **спекулятивный скан** `ScanTypeArgumentList:6227` (на reset point, `6197-6200`), классифицирует `DefiniteTypeArgumentList` / `PossibleTypeArgumentList` (принимается только с `NameOptions.InTypeList`) / `NotTypeArgumentList` (`6202`); затем `ParseTypeArgumentList:6545` (open generic `List<,>` через `IsOpenName:6548`, ранний выход при «разбивающих» токенах `6572-6598`).
- Вспомогательные: `IsPossibleType():7154` (predefined | true-identifier), `IsTrueIdentifier():6019,6038` (identifier, не-`partial`, не-where-clause, не-query-keyword), `ParseIdentifierToken:6059` (special-кейсы `partial`/`await` в async), `ParseIdentifierName:6045`, `ParseTypeOrVoid:7541`.

---

## 2. Таблица токенов

Где живёт классификация (всё в `src/Compilers/CSharp/Portable/Parser/` + `Syntax/`):

| Что | Где |
|---|---|
| Вход лексера | `Lexer.cs:248` `Lex(LexerMode)` — режимы Syntax/DebuggerSyntax → `QuickScanSyntaxToken() ?? LexSyntaxToken()` (`258`); Directive → `LexDirectiveToken:2466`; XML-режимы для doc-комментариев (`263-289`) |
| Токен с тривиумами | `Lexer.cs:292` `LexSyntaxToken` (leading trivia → `ScanSyntaxToken` → trailing trivia) |
| **Диспетчер токена** | `Lexer.cs:425` `ScanSyntaxToken` — switch по первому символу: литералы строк/char `\"`/`'` (`440-443`), все пунктуация с мульти-знаковым lookahead через `TextWindow.TryAdvance` (`445-608`: `::` `490`, `??=` `552-554`, `=>` `512`, `->` `569`, `<<`/`<<=` `599-602` и т.д.), `@`-verbatim (`610-632`), `$`-интерполяция (`634-638`), identifier-старт `_`/`a-z`/`A-Z` (`649-651`), цифры (`654`), `\uXXXX`-escape в начале identifier (`658-667`), `#` — в trivia (раздел 5), незнакомый символ → error-токен (лимит 200 подряд, `713-714`) |
| **Identifier/keyword** | `Lexer.cs:1815` `ScanIdentifierOrKeyword`: `ScanIdentifier:1291` (fast path `1321` / slow path `1421`); затем: если не `@`-verbatim и без `\u`-escape — `LexerCache.TryGetKeywordKind:1842` → `SyntaxFacts.GetKeywordKind` (`Syntax/SyntaxKindFacts.cs:880`, switch по строке: все **резервированные** ключевые слова); если результат — **contextual keyword** (`SyntaxFacts.IsContextualKeyword`, `SyntaxKindFacts.cs:1253`) → `Kind = IdentifierToken`, `ContextualKind = <keyword>` (`1846-1850`). `@ident`/escape → всегда `IdentifierToken` (`1824`, `1858-1861`) |
| Список contextual keywords | `Syntax/SyntaxKindFacts.cs:1337` `GetContextualKeywordKind(string)` — `yield partial from group join into let by where get set add remove orderby alias on equals ascending descending assembly module type field method param property typevar global async await when nameof _ var and or not with init record managed unmanaged required scoped file allows extension union closed safe` |
| **Predefined types** | `Syntax/SyntaxKindFacts.cs:316` `IsPredefinedType(SyntaxKind)` — 17 keyword-типов (перечислены в 1.5) |
| Числовые литералы | `Lexer.cs:844` `ScanNumericLiteral` (dec/hex/bin/oct, разделители `_`, суффиксы; `ScanNumericLiteralSingleInteger:801`); `.`+цифра — fp-число с особой обработкой `..0` (range, `450-479`) |
| String/char/verbatim/interpolated | `Lexer_StringLiteral.cs:14` `ScanStringLiteral` (dispatch char vs string), `:192` verbatim, `:254` interpolated (+ `Lexer.Interpolation.cs`); raw strings — `Lexer_RawStringLiteral.cs:51,101,133` (C# 11) |
| Trivia (пробел/комментарии) | `Lexer.cs:1872` `LexSyntaxTrivia`: whitespace (`1879-1904`), `//` и `/* */` (`ScanMultiLineComment:2208`), `#`-директивы (`1989`, `2336`), `#region` и т.д. |
| Unicode-классификация | `Parser/CharacterInfo.cs`: `IsWhitespace:116` (включая NBSP, `\uFEFF`, `^Z` — legacy), `IsNewLine:149` (CR/LF/`U+0085`/`U+2028`/`U+2029`), `IsIdentifierStartCharacter:169` / `IsIdentifierPartCharacter:178` / `IsValidIdentifier:186` → делегируют в `src/Compilers/Core/Portable/InternalUtilities/UnicodeCharacterUtilities.cs:15,49,91` (Unicode-категории Lu/Ll/Lt/Lm/Lo, Mn/Mc/Me, Nd, Pc, Zs, Cf — форматные символы отбрасываются, `CharacterInfo.cs:200` `ContainsDroppedIdentifierCharacters`) |
| Кэш слов | `Parser/LexerCache.cs:174` `TryGetKeywordKind` (ObjectPool-словарь поверх `GetKeywordKind`) |

**Как решается identifier vs keyword при парсинге** (важно для `!KeywordSet Identifier` в T1.2):
- Лексер уже «знает»: резервированное слово → собственный `SyntaxKind` (IntKeyword, ClassKeyword...); контекстное слово → `IdentifierToken` + `ContextualKind`; `@word` → всегда identifier.
- Парсер принимает решение по `ContextualKind` + **позиционным хэurистикам**:
  - `partial` — `IsCurrentTokenDefinitelyPartialModifier:1637` (lookahead на старт type/member declaration);
  - `async` — `ShouldContextualKeywordBeTreatedAsModifier:1501`;
  - `where` — `IsCurrentTokenWhereOfConstraintClause:2234` (`where <id> :`);
  - `global` — только перед `using` (`ParseUsingDirective:949`, `ParseNamespaceBodyWorker:674-685`, `IsPossibleMemberName:1743`);
  - `alias` — только в `extern alias` (`918`);
  - `record` — `tryScanRecordStart:1931` + гейт версии `IsFeatureEnabled(IDS_FeatureRecords):1627`;
  - `var` — только в контексте statement (в нашей фазе не нужен);
  - `await` — identifier, кроме async-контекста (`IsInAsync:14434`, ошибка `ERR_BadAwaitAsIdentifier` в `ParseIdentifierToken:6079-6082`);
  - `field` — C#14, `IsCurrentTokenFieldInKeywordContext:6097`.
- Для PEG: резервированные ключевые слова = отдельные токены терминалов; контекстные = `Identifier` + правила вида `!<контекст-lookahead> Identifier`, либо отдельный contextual-токен, который парсер «переводит» в keyword (`ConvertToKeyword`, `SyntaxParser.cs:1104`).

---

## 3. Гейтация по CSharpVersion

**Главный вывод:** в текущем `main` парсер **не гейтит фичи по версии языка** (кроме случаев, где версия влияет на *формы* синтаксиса или на дисамбигуацию). Комментарий в таблице: «PREFER reporting diagnostics in binding when diagnostics do not affect the shape of the syntax tree» (`MessageID.cs:494`).

Идиомы, которые **остались в парсере** (`LanguageParser.cs`):
- `IsFeatureEnabled(MessageID.IDS_FeatureX)` — только для дисамбигуации contextual-keywords:
  - `record`/`union`: `1627-1628` (`IsEnabledRecordOrUnionKeyword`);
  - `extension`: `3400` (`IsExtensionContainerStart` — принимает `extension <` даже в старых версиях ради рекавери);
  - `file`/`closed`/`required`/`safe` в `ParseModifiers`: `1411-1437`;
  - `field`: `6101` (`IsCurrentTokenFieldInKeywordContext`).
- `CheckFeatureAvailability(node, MessageID.IDS_FeatureX)` — единственная точка, где парсер сам ругается на версию: **generics** — `ParseTypeArgumentList:6550` (`open = CheckFeatureAvailability(open, MessageID.IDS_FeatureGenerics)`). Механика: `CheckFeatureAvailability` (`SyntaxParser.cs:1147`) → `GetFeatureAvailabilityDiagnosticInfo` (`Errors/MessageID.cs:466`) → `options.IsFeatureEnabled(feature) ? null : error(ERR_xxxx, RequiredVersion)`.
- `#langversion` — `DirectiveParser.cs:337-353`.

**Где на самом деле живёт гейт** (для Stage 3, если решим ругаться на версию в парсере): полная таблица «MessageID → LanguageVersion» — **`Errors/MessageID.cs:486-746`** (`RequiredVersion()`). По версиям (только то, что релевантно плану; `// semantic check` — значит в upstream ошибка в байндере):

| Версия | Фичи (MessageID) |
|---|---|
| C# 2 | `IDS_FeatureGenerics` (парсер, `6550`), `GlobalNamespace` (`global::`), `PartialTypes`, `PropertyAccessorMods`, `ExternAlias`, `Nullable`, `Pragma`, `StaticClasses`, `Iterators`, `Default`, `AnonDelegates`, `FixedBuffer` |
| C# 3 | `ObjectInitializer`, `CollectionInitializer`, `Lambda`, `ExtensionMethod`, `ImplicitLocal` (`var`), `AutoImplementedProperties`, `PartialMethod`, `AnonymousTypes`, `ImplicitArray`, `QueryExpression` |
| C# 4 | `Dynamic` (binder), `OptionalParameter`, `NamedArgument`, `TypeVariance` (`in`/`out` в type params) |
| C# 5 | `Async` (binder) |
| C# 6 | `InterpolatedStrings`, `NullPropagatingOperator` (`?.`), `Nameof`, `UsingStatic`, `ExpressionBodiedMethod/Property/Indexer`, `AutoPropertyInitializer`, `ReadonlyAutoImplementedProperties`, `DictionaryInitializer`, `ExceptionFilter`, `AwaitInCatchAndFinally` |
| C# 7 | `Tuples`, `LocalFunctions`, `PatternMatching` (binder), `OutVar`, `RefLocalsReturns`, `DigitSeparator`, `BinaryLiteral`, `ExpressionBodiedAccessor`, `ExpressionBodiedDeOrConstructor`, `Discards`, `ThrowExpression` |
| C# 7.1–7.3 | `GenericPatternMatching`, `InferredTupleNames`, `AsyncMain`, `RefStructs`, `RefExtensionMethods`, `PrivateProtected`, `ReadOnlyReferences`, `RefFor`, `RefReassignment`, `EnumGenericTypeConstraint`... |
| C# 8 | `SwitchExpression`, `UsingDeclarations`, `RecursivePatterns`, `RangeOperator` (`..`), `IndexOperator` (`^`), `AsyncUsing`, `NullableReferenceTypes`, `DefaultInterfaceImplementation`, `StaticLocalFunctions`, `ReadOnlyMembers`, `CoalesceAssignmentExpression`, `UnconstrainedTypeParameterInNullCoalescingOperator` |
| C# 9 | `Records`, `InitOnlySetters`, `TopLevelStatements`, `FunctionPointers`, `And/Not/Or/Parenthesized/Type/Relational Pattern`, `NativeInt`, `MemberNotNull`, `CovariantReturnsForOverrides`, `StaticAnonymousFunction`, `ExtendedPartialMethods`, `DefaultTypeParameterConstraint` |
| C# 10 | `FileScopedNamespace` (binder), `GlobalUsing` (binder), `RecordStructs`, `WithOnStructs`, `PositionalFieldsInRecords`, `ParameterlessStructConstructors`, `StructFieldInitializers`, `InferredDelegateType` |
| C# 11 | `RawStringLiterals`, `RequiredMembers` (binder), `RefFields` (binder), `FileTypes` (binder), `AutoDefaultStructs`, `ListPattern`, `SpanCharConstantPattern`, `Utf8StringLiterals`, `GenericAttributes` |
| C# 12 | `PrimaryConstructors` (declaration-table check), `CollectionExpressions` (binder), `InlineArrays`, `LambdaOptionalParameters`, `UsingTypeAlias`, `RefReadonlyParameters`, `InstanceMemberInNameof` |
| C# 13 | `StringEscapeCharacter` (lexer), `RefStructInterfaces`, `PartialProperties`, `OverloadResolutionPriority`, `ParamsCollections`, `LockObject`, `AllowsRefStructConstraint`, `RefUnsafeInIteratorAsync` |
| C# 14 | `Extensions` (extension members — дисамбигуация в парсере, `3400`), `FieldKeyword` (дисамбигуация, `6101`), `PartialEventsAndConstructors`, `SimpleLambdaParameterModifiers`, `NullConditionalAssignment`, `UserDefinedCompoundAssignmentOperators`, `UnboundGenericTypesInNameof`, `FirstClassSpan` |

Для нашей метаграмматики это значит: **версионные файлы `CsN.grammar` (C#1..C#14) должны добавлять альтернативы без гейтов в парсере** — парсер парсит union по всем версиям, а «это не C# 2» — ошибка уровня семантики/анализа (или отдельная фаза). Исключения, где гейт реально меняет синтаксический разбор: contextual-ключевые слова (`record`, `extension`, `file`, `closed`, `required`, `safe`, `field`) — в старых версиях они **идентификаторы** (иначе сломается разбор валидного кода, напр. `class file { }` в C#12).

---

## 4. Тесты

**Локация в этом чекате:** `C:\RSDN\roslyn\src\Compilers\CSharp\Test\Syntax\` (проект `Microsoft.CodeAnalysis.CSharp.Syntax.UnitTests.csproj`), xUnit. Подпапки: `Parsing/` (78 файлов), `LexicalAndXml/` (10), `Diagnostics/` (3), `IncrementalParsing/` (12), `Generated/`, `Syntax/Mocks`. В чекате **нет** `.stree`/`.diag`/`.cspans`-файлов — VerifyTest-подход (`TestFile` из upstream `tests/Compilers/CSharp/TestData/Parser/`) здесь не представлен.

**Паттерн тестов** (xUnit-асерт, не VerifyTest):
```csharp
var text = "extern alias a;";
var file = this.ParseFile(text);           // SyntaxFactory.ParseSyntaxTree(text, TestOptions.Regular)
Assert.Equal(0, file.Errors().Length);
Assert.Equal(text, file.ToString());       // round-trip
// error recovery:
UsingTree(text, Diagnostic(ErrorCode.ERR_RbraceExpected, "").WithLocation(4, 6), ...);
```
Базовый класс `ParsingTests` с `ParseFile`/`ParseTree` (напр. `Parsing/DeclarationParsingTests.cs:23-26`, `Parsing/ParsingErrorRecoveryTests.cs:20-23`).

### Корректный код (для T1.3/T1.4)

| Область | Файл | Размер | Ключевые тесты |
|---|---|---|---|
| Compilation unit: extern alias, using, namespace | `Parsing/DeclarationParsingTests.cs` | 838 КБ, ~19346 строк | `TestExternAlias:29`, `TestUsing:52`, `TestUsingStatic:74`, `TestUsingAliasName:207`, `TestNamespace:560`, `TestFileScopedNamespace:582`, `TestNamespaceWithDottedName:603`, `TestNamespaceWithUsing:625`, `TestNamespaceWithExternAlias:670`, `TestNamespaceWithNestedNamespace:739`; битые имена `5677-5747`, `..`-кейсы `8437-8773` |
| using-директивы (широко) | `Parsing/UsingDirectiveParsingTests.cs` | 206 КБ | все формы `using [static] [unsafe] [alias=] name;` + ошибки |
| Заголовки class/interface/struct/delegate + constraints + base lists | `Parsing/DeclarationParsingTests.cs` | — | class: `TestClass:770`, модификаторы `796-957`, атрибуты `958-1039`, base `1040-1102`, constraints `1103-1592`; interface `1593`, `TestGenericInterfaceWithAttributesAndVariance:1648`; struct `1680`; delegate `1993-2472`; `TestPartialEnum:5794` |
| Члены (контекст заголовков) | `Parsing/MemberDeclarationParsingTests.cs` | 1.0 МБ | `TypeDeclaration:~`, `EnumConstraint_On*:9153-9342`, `RequiredModifier*`, `ExtraTypeOnlyMembers_AfterEnum:12078` |
| Имена: qualified/alias/generic, массивы, указатели, nullable | `Parsing/NameParsingTests.cs` | 70 КБ | `TestDottedName`, `TestAliasedName`, `TestGenericName`, `TestOpenNameWithAComma`, `TestNullableTypeName`, `TestPointerTypeName(W)`, `TestArrayTypeName`, `TestMultiDimensionalArrayTypeName`, `TestMultiRankedArrayTypeName`, `TestKnownTypeNames`, Unicode: `TestFormattingCharacter`, `TestSoftHyphen` |
| Типы + generic-аргументы (disambiguation) | `Parsing/TypeArgumentListParsingTests.cs` | 193 КБ | `TestPredefinedType`, `TestArrayType`, `TestPredefinedPointerType`, `TestQualified/AliasName`, `TestNullableTypeWithComma/GreaterThan`, `TestGenericArgWithComma_01..04`, `TestGenericArgWithGreaterThan_01..05` (разбор `a < b > c` vs generic), `TestGenericWithExtraCommasAndMissingTypes1..8` |
| Nullable-типы | `Parsing/NullableParsingTests.cs` | 96 КБ | `int?`, `T?`, `T?[]`, `T??` и т.д. |

### Сломанный код / error recovery

| Файл | Размер | Содержание |
|---|---|---|
| `Parsing/ParsingErrorRecoveryTests.cs` | 415 КБ, ~8791 строк | Главный файл: garbage в enum (`2662-2800`), namespace, type headers, missing identifiers и т.д. Паттерн `UsingTree(text, Diagnostic(...).WithLocation(l, c))` |
| `Parsing/ParserErrorMessageTests.cs` | 275 КБ | Конкретные коды ошибок парсера (`CS0267ERR_PartialMisplaced_Enum:618`, `CS1041RegressKeywordInEnumField:2977`, `CS1675ERR_InvalidGenericEnumNowCS7002:5126`...) |
| `Parsing/DeclarationParsingTests_MissingIdentifiers.cs` | 305 КБ | Missing identifiers во всех объявлениях (`TypeMissingIdentifier_Enum01:1757`) |
| `Parsing/ModifierParserRecoveryTests.Partial.cs` (+ `.AlwaysModifier.cs`) | ~114 КБ | Рекавери `partial`/модификаторов |
| `Parsing/ParserRegressionTests.cs` | 40 КБ | Регрессии |
| `LexicalAndXml/LexicalTests.cs` | 183 КБ | Лексер: литералы, идентификаторы, ключевые слова |
| `LexicalAndXml/LexicalErrorTests.cs` | 83 КБ | Лексические ошибки |
| `LexicalAndXml/PreprocessorTests.cs` | 191 КБ | `#if/#define/#line/...` |
| `Diagnostics/DiagnosticTest.cs` | 155 КБ | Точность позиций diagnostics |

**Представительная подмножество** (по размеру/покрытию): из `DeclarationParsingTests.cs` взять блоки `29-231` (extern/using), `560-739` (namespace), `770-1593` (class), `1593-2472` (interface/struct/delegate); `NameParsingTests.cs` целиком (70 КБ — компактно покрывает все типы); `TypeArgumentListParsingTests.cs` выборочно (`TestGenericArgWith*`); из recovery — `ParsingErrorRecoveryTests.cs:2662-2800` (enum) + namespace-кейсы.

---

## 5. Подводные камни для PEG-грамматики

1. **Препроцессор — вложенный язык внутри trivia.** `#` обрабатывается в trivia-проходе лексера (`Lexer.cs:1989`, `LexDirectiveAndExcludedTrivia:2336`), директивы парсит отдельный `DirectiveParser` (`DirectiveParser.cs`), а **условие `#if` — собственный мини-парсер выражений** (`ParseExpression:770` → `ParseLogicalOr:775`/`ParseLogicalAnd:788`/`ParseEquality:801`/`ParseLogicalNot:814`/`ParsePrimary:825`) с **собственной оценкой** (`EvaluateBool:863`, `Evaluate:874`) и стеком `DirectiveStack` (`Directives.cs`, `Add:230`); отброшенный код становится `ExcludedCodeTrivia`. Для T1.x минимально достаточный вариант: `#`-строки = trivia (как в C# 1.0 без препроцессора); полный `#if` — отдельная подзадача.
2. **Unicode-идентификаторы.** Start: Lu/Ll/Lt/Lm/Lo/`_`; часть: + Mn/Mc/Me/Nd/Pc (Cf отбрасывается); `\uXXXX`-escape **внутри** identifier (`Lexer.cs:658`, `PeekCharOrUnicodeEscape`); `@`-verbatim (`610`); форматные символы (Cf, напр. `U+FEFF`) вырезаются из значения (`CharacterInfo.cs:200`). В терминалах CsNitra это DFA с классами Unicode, а не ASCII-набор.
3. **Contextual keywords — главная зона PEG-амбигуозности.** Лексер выдаёт их как `IdentifierToken + ContextualKind`; решение о «keyword-ности» принимается парсером с lookahead (`partial` — `1637`, `async` — `1501`, `where` — `2234`, `global` — `949/674`, `alias` — `918`, `record` — `1931`, `await` — `14434`). В CsNitra: либо `!KeywordSet Identifier` + правила-переходы (`ConvertToKeyword`-аналог), либо отдельные contextual-терминалы с позиционными ограничениями. Не забыть: в **старых** версиях `record`/`file`/`extension`/... — обычные идентификаторы.
4. **Longest-match vs ordered choice.** Roslyn в амбигуозных точках использует **спекулятивный разбор + reset points** (`GetResetPoint`/`Reset`, `SyntaxParser.cs:158/170`; `GetDisposableResetPoint`), т.е. фактически «попробовать и откатиться». Ключевые места:
   - **generic vs сравнение**: `ScanTypeArgumentList:6227` — сканирует `<...>` и решает Definite/Possible/Not по *последующим* токенам (`.` `(` `,` `>` `?` `*` `[` `)` и т.п.). В longest-match PEG это «естественно» разрешится, только если альтернатива «бинарное выражение `<`» короче — проверять на `a < b > c`, `List<int> x = a < b;`.
   - **attribute vs collection expression**: `TryParseAttributeDeclaration:1103` + `IsPossibleAttributeDeclaration:1031` (C# 12; в нашей фазе атрибутов достаточно «`[` + identifier/`word:` + `]`»).
   - **`using`**: directive vs statement/declaration — `ParseNamespaceBodyWorker:661` (lookahead `(` / possible-top-level-using-local).
   - **`extern alias` vs extern local function**: 4-токенный lookahead `ScanExternAliasDirective:905`.
   - **`ref`**: модификатор vs return type — `1380-1400` + `IsTypeFollowedByMemberName:1732` (вызов из `shouldConsumeRefAtTopLevel:1465`).
   - **`T?`** в `is`/`as`: nullable type vs условное выражение — `TryEatNullableQualifierIfApplicable:7682` (lookahead до `:`).
5. **Рекавери пронизывает всё** (в T1.x в основном за границей, но структура должна это выдерживать): missing tokens (`EatToken(kind)` → `CreateMissingToken:552`), skipped-тривиумы (`AddTrailingSkippedSyntax:980`), **терминаторы** (`TerminatorState:58` + `IsTerminator():94`) — имплицитный «контекст», останавливающий runaway-циклы; `SkipBad*Tokens`-хелперы; `IsMakingProgress:1178` (guard против неограниченных циклов — в PEG это обязанность грамматика: каждый цикл должен гарантированно потреблять токен); post-pass переноса «сбежавших» членов (`ParseNamespaceBody:410-467`).
6. **`TerminatorState` как имплицитный параметр правил.** Флаги (`IsEndOfTypeSignature`, `IsPossibleMemberStartOrStop`, ...) выставляются на время под-разбора (`_termState |= ...; try { ... } finally { _termState = saveTerm; }`, напр. `1804-1919`). В PEG это эквивалентно параметрам/предикатам правил («в контексте base list `where` заканчивает список типов», `2188-2189`).
7. **`void` в типах** принимается с ошибкой (`ParseUnderlyingType:7975-7978`), `..` в именах — только рекавери (`7036`), `::` вне `Ident::` — рекавери в `.` (`7063-7088`): грамматику лучше писать «щедрую» (принять + пометить), как Roslyn.
8. **Форма parse tree**: `CompilationUnit(externs, usings, attrs, members, eof)`; namespace = `NamespaceDeclaration` vs `FileScopedNamespaceDeclaration`; type = единый узел с `Keyword` (class/struct/interface/record/extension/union) + опц. `recordModifier` — наш parse tree (как в CsNitra) должен решить, дублировать ли эту унификацию или оставить отдельные правила.
9. **Top-level statements** (C# 9) живут в том же `ParseNamespaceBodyWorker` через `ParseMemberDeclarationOrStatement:2574` — для T1.x можно не парсить, но `;`-форм namespace (file-scoped, C# 10) проходит тот же код.
10. **Стек/рекурсия**: парсер явно считает `_recursionDepth` + `StackGuard` (`236-245`, `3225-3229`) и глобальный fallback `CreateForGlobalFailure:223` — в PEG-движке это наша ответственность (ограничение вложенности / итеративный разбор).

---

## Чек-лист для T1.2–T1.4

### T1.2 — Терминалы (лексер)
Зеркалировать:
- `Lexer.cs:425-730` — `ScanSyntaxToken`: таблица пунктуации (мульти-знаковые через `TryAdvance`), `@`-идентификаторы, цифры, `\u`-escape, незнакомые символы.
- `Lexer.cs:1815-1870` — `ScanIdentifierOrKeyword` + `ScanIdentifier:1291/1321/1421`; `LexerCache.cs:174`.
- `Syntax/SyntaxKindFacts.cs:880` (`GetKeywordKind` — список резервированных) и `:1337` (`GetContextualKeywordKind` — список контекстных).
- `Syntax/SyntaxKindFacts.cs:316` — `IsPredefinedType` (17 типов).
- `Parser/CharacterInfo.cs:116-189` + `Core/Portable/InternalUtilities/UnicodeCharacterUtilities.cs:15-91` — Unicode-классы.
- `Lexer.cs:844/801` — числовые литералы; `Lexer_StringLiteral.cs:14/192` — string/char/verbatim (interpolated/raw — не в фазе).
- `Lexer.cs:1872` + `:2208` — trivia: пробел, `//`, `/* */` (`#`-директивы — trivia-заглушка, полный вариант — позже).
- Тесты: `Test/Syntax/LexicalAndXml/LexicalTests.cs`, `LexicalErrorTests.cs`; `Parsing/NameParsingTests.cs` (Unicode-кейсы `TestFormattingCharacter`, `TestSoftHyphen`).

### T1.3 — Компиляционная единица, using/extern, namespace
Зеркалировать:
- `LanguageParser.cs:180-205` — `ParseCompilationUnitCore`.
- `LanguageParser.cs:562-846` — `ParseNamespaceBodyWorker` (цикл, порядок externs→usings→members, `global using` `674-685`, `ScanExternAliasDirective:905`, using-дисамбигуация `661`); `410-467` — post-pass (опционально для фазы).
- `LanguageParser.cs:247-328` — `ParseNamespaceDeclarationCore` (block + `;`-форм).
- `LanguageParser.cs:918-932` — `ParseExternAliasDirective`.
- `LanguageParser.cs:942-1010` — `ParseUsingDirective` (`global`/`static`/`unsafe`/alias/qualified name).
- `LanguageParser.cs:869-900` — `IsPossibleNamespaceMemberDeclaration`, `IsPossibleStartOfTypeDeclaration:331`, `IsTypeModifierOrTypeKeyword:337`.
- Тесты: `Test/Syntax/Parsing/DeclarationParsingTests.cs:29-231, 560-739, 5677-5747, 8437-8773`; `UsingDirectiveParsingTests.cs`; `ParsingErrorRecoveryTests.cs` (namespace/using-кейсы).

### T1.4 — Объявления типов (class/struct/enum/interface/delegate) + типы
Зеркалировать (заголовки):
- `LanguageParser.cs:1756-1787` — `ParseTypeDeclaration` (диспатч).
- `LanguageParser.cs:1789-1930` — `ParseMainTypeDeclaration` (name, generic clause, base list, constraints, `{}`-тело с циклом `CanStartMember:2427`/`IsTerminator:94`; `tryScanRecordStart:1931` — зафиксировать, но record не в фазе).
- `LanguageParser.cs:2170-2232` — `ParseBaseList`; `2234-2248` — `where`-lookahead; `2242-2423` — constraint clauses.
- `LanguageParser.cs:6104-6179` — `ParseTypeParameterList` / `IsStartOfTypeParameter` / `ParseTypeParameter` (variance in/out).
- `LanguageParser.cs:5835-5866` — `ParseDelegateDeclaration`; `5868-5978` — `ParseEnumDeclaration` / `ParseEnumMemberDeclaration` (включая запрет generic-enum и один базовый тип).
- `LanguageParser.cs:1347-1499` — `ParseModifiers` + `GetModifierExcludingScoped:1286` + `IsCurrentTokenDefinitelyPartialModifier:1637` (в фазе: только reserved-модификаторы + `partial`; `async`/`ref`/contextual — позже).
- `LanguageParser.cs:1031-1281` — атрибуты (`ParseAttributeDeclarations`, `TryParseAttributeDeclaration`, `ParseAttribute`, `ParseAttributeArgumentList`, `ParseAttributeArgument`).
- `LanguageParser.cs:3223-3309` — путь `ParseMemberDeclarationCore` до `IsTypeDeclarationStart:2483` (для вложенных типов).

Зеркалировать (типы):
- `LanguageParser.cs:7566-7680` — `ParseType`/`ParseTypeCore` (postfix: `?` `*` `[...]`; в фазе: без ref-префикса и tuple/function-pointer).
- `LanguageParser.cs:7969-8001` — `ParseUnderlyingType` (predefined | qualified | `::`-рекавери).
- `LanguageParser.cs:7860-7918` — `ParseArrayRankSpecifier` (multi-dim `[,,]`).
- `LanguageParser.cs:8173-8182` — `ParsePointerTypeMods`.
- `LanguageParser.cs:7023-7093` — `ParseQualifiedName`/`ParseQualifiedNameRight`; `6182-6218` — `ParseSimpleName`; `6227-6542` — `ScanTypeArgumentList` (generic-vs-`<` хэurистика); `6545-6650` — `ParseTypeArgumentList`.
- `LanguageParser.cs:6019-6090` — `IsTrueIdentifier`/`ParseIdentifierToken`/`ParseIdentifierName`.
- Тесты: `Test/Syntax/Parsing/DeclarationParsingTests.cs:770-1593 (class), 1593-1680 (interface), 1680-1993 (struct), 1993-2472 (delegate), 5794 (partial enum)`; `NameParsingTests.cs` (целиком); `TypeArgumentListParsingTests.cs` (`TestPredefinedType`, `TestArrayType`, `TestPointerTypeName*`, `TestGenericArgWithComma_01..04`, `TestGenericArgWithGreaterThan_01..05`); `NullableParsingTests.cs`; `MemberDeclarationParsingTests.cs` (выборочно); `ParsingErrorRecoveryTests.cs:2662-2800` (enum recovery) + type-header recovery.
