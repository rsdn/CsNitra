# T3.5.2 — Интерполированные строки: реализация + тесты

Спецификация: `docs/InterpolatedStringGrammar.md` (дизайн T3.5.1).

## Статус: выполнено

## Базовая линия (до изменений)
- `dotnet test CSharpGrammarTests` → **223 passed / 0 failed**.
- Roslyn-тесты Cs1 (`Cs1Roslyn*`) — зелёные (часть 223).

## Что сделано
- [x] `Cs1.grammar`: `precedence` + временное `Expression`/`Primary`/`PostfixOp`
- [x] `Cs6.grammar` (новый): 5 правил интерполяции + `VerbatimInterpolatedPrefix` + переобъявление `Expression`
- [x] `CSharpGrammar.csproj`: `Cs6.grammar` в `<EmbeddedResource>`
- [x] `CSharpTerminals.cs`: +7 hand-written терминалов, −2 интерполированных терминала (фабрика/поле/record/вход в `GetAll()`)
- [x] `StringLiteralScanner.cs`: удаление интерполированных путей; `TryScanEscape` → `internal` (переиспользуется `InterpolatedRegularEscape`/`RegularFormatText`)
- [x] `EmbeddedGrammar.cs`: `LoadCs6Grammar()` + общий `Load(string)`
- [x] Новый файл тестов `InterpolatedStringTests.cs` (11 методов, §6)
- [x] `CSharpTerminalsTests.cs`: удаление старых интерполированных сканер-тестов (25)
- [x] `dotnet build` (решение) + `dotnet test` (все 4 проекта) — зелёные

## Решения / отклонения
1. **`PostfixOp` — SeparatedList вместо ручной группы** (дизайн §3). Дизайн:
   `Indexer = "[" (Expression ("," Expression)*)? "]"`. CsNitra-`AstSimplifier` не выводит
   имена для анонимных вложенных последовательностей → использован `SeparatedList`
   `"[\" (Expression; \",\")* \"]\"` (CanBeEmpty, trailing-`Forbidden`). Семантически
   эквивалентно (пустой список / `E` / `E, E` / …).
2. **`VerbatimInterpolatedPrefix` — отдельное правило** (дизайн §2.4). Дизайн:
   `( "$@\"" | "@$\"" )` — группа с `|` внутри не выразима в языке текстовых грамматик →
   вынесено в правило `VerbatimInterpolatedPrefix = | "$@\"" | "@$\"";`. Задокументировано
   комментарием в `Cs6.grammar`.
3. **Дизайн-опечатка §2.7 исправлена в реализации.** В дизайне D=3 raw-дыры (`HoleC`/`HoleB`/`HoleA`)
   закрывались `"}}}}"` (4 скобки). По обобщению §2.7 (линейная нота 356-359) и по аналогии
   с D=2 (§2.6, закрывающие = D скобок) дыра уровня D закрывается **D** скобками → `"}}}"`
   (3). Реализация исправлена на 3; тест `$$$"""{{{x}}}"""` подтверждает. (Сам дизайн-документ
   не менялся — только реализация; опечатка в §2.7 зафиксирована здесь.)
4. **Char-литералы в `!`-предикатах → string-литералы** (дизайн §2.5–2.7). Дизайн писал
   `!'"'`/`!'}'` (char-литералы), но язык текстовых грамматик CsNitra поддерживает только
   double-quoted string-литералы → `!"\""`/`!"}"`.

## Верификация
- `dotnet build Nitra.sln` (Debug/x64, `--no-incremental`) → **успех, 0 ошибок**.
- `CSharpGrammarTests` → **211 passed / 0 failed** (223 − 25 удалённых сканер-тестов + 13 новых
  интерполяционных = 211).
- `ParserTests` → **324 passed / 0 failed / 2 skipped** (Roslyn-тесты Cs1 зелёные).
- `RegexTests` → **9 passed / 0 failed**.
- `WiWorkflowTests` → **1 passed / 0 failed**.

