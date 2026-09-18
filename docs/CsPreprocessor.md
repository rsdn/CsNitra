# CsPreprocessor — препроцессор C# (интерпретатор на CsNitra)

## ⚠️ ВАЖНО: скилл plan-execution

Субагент-оркестратор: **после ЛЮБОГО сжатия сессии ВСЕГДА перечитывай скилл `plan-execution`**
(tool `skill`, name `plan-execution`) перед следующим шагом оркестрации — особенно перед
верификацией, коммитом и перезапуском умершего субагента. Не полагайся на память о правилах
оркестрации: один субагент на подпункт, progress-файл у каждого субагента, верификация
(build + tests + diff) перед коммитом, умерший субагент → перезапуск с учётом его progress-файла.

## Цель

Препроцессор C#, чтобы можно было парсить **реальные .cs-файлы**. Препроцессор — это
**интерпретатор** (не просто парсер):

- **Вход**: исходный текст + набор символов из командной строки (`/define`).
- **Выход**: `PreprocessResult` = `{ string Text; IReadOnlyList<Diagnostic> Diagnostics; IReadOnlyList<LineDirective> LineDirectives; }`.
- `Text` — файл **без препроцессорных директив** (активные директивные строки и неактивные
  области вычищены), **сохраняющий все позиции**.
- **Гарантия позиций**: `Text.Length == source.Length` и `Text[i] == source[i]` для каждого
  активного символа; вычищенные символы заменены пробелом, переносы строк (`\n`/`\r`)
  сохраняются. Следовательно, препроцессорный offset == исходный offset (**identity mapping**),
  и **сообщения об ошибках, полученные при парсинге `Text`, совпадают с позициями в реальном
  файле**. **На это нужны ОТДЕЛЬНЫЕ тесты** (Этап 3).

## Ключевые решения

- **D1 — Интерпретатор.** Прогон даёт готовый чистый файл + диагностики. Не «разметка
  неактивных областей в дереве» (как в Roslyn), а **реальный новый текст**.
- **D2 — Сохранение позиций через same-length blanking.** Каждый вычищенный символ → пробел,
  `\n`/`\r` сохраняются. `Text.Length == source.Length`. Identity offset mapping — **отдельная
  offset-карта не нужна**. Диагностика PEG-парсера на `Text` уже в координатах исходника.
- **D3 — На CsNitra, без AST, через visitor.** Грамматика препроцессора — текст CsNitra
  (декларатив). Интерпретация — **посетитель** (`ISyntaxVisitor`), который обходит дерево
  **в порядке исходника** и **на лету** вычисляет препроцессорные символы (stack) и
  active/inactive. Отдельного hand-written AST нет — только parse tree + visitor.
- **D4 — Семантика как в Roslyn.** `DirectiveStack` (символы + ветки), `BranchTaken` (первая
  истинная ветка), условие `#if` **без оператора `defined`** (голый идентификатор = «определён
  ли»), `#define`/`#undef`/`#error`/`#warning` действуют только в активной области,
  `#region`/`#endregion` не влияют на active/inactive. Подробности — в разделе «Семантика».
- **D5 — `#line` = display remap.** `#line` меняет только **отображаемый** файл/строку, не
  offsets. Препроцессор **запоминает** активные `#line` в `LineDirectives` (для будущего
  отображения); сам `Text` из-за `#line` не меняется.

## Как это работает в Roslyn (результат исследования)

Roslyn **не** строит новый текст и **не** имеет отдельного preprocessing-прохода: это
**однопроходный lexer**, который на лету решает active/inactive и хранит **весь** текст в
дереве; неактивные области = один trivia `DisabledText`. Состояние = `DirectiveStack` +
`Options.PreprocessorSymbols` (символы из командной строки). Ключевые файлы (`C:\RSDN\roslyn`):

- `src/Compilers/CSharp/Portable/Parser/Lexer.cs` — распознавание директив (`1975-2010`),
  обход неактивной области `LexExcludedDirectivesAndTrivia`/`LexDisabledText` (`2336-2464`),
  обновление `DirectiveStack` (`2394`).
- `src/Compilers/CSharp/Portable/Parser/DirectiveParser.cs` — парсинг каждой директивы;
  грамматика + оценка условия `#if` (`770-927`).
- `src/Compilers/CSharp/Portable/Parser/Directives.cs` — `Directive`/`DirectiveStack`
  (`IsDefined`, `PreviousBranchTaken`, `CompleteIf`).
- `src/Compilers/CSharp/Portable/Syntax/CSharpLineDirectiveMap.cs` — отображение `#line`.
- Тесты: `src/Compilers/CSharp/Test/Syntax/LexicalAndXml/DisabledRegionTests.cs`,
  `PreprocessorTests.cs`.

**Мы инвертируем представление** (наш PEG читает линейный поток): вместо «весь текст +
trivia» — «новый текст + identity mapping». Семантику (D4) повторяем 1:1.

### Семантика (D4, из Roslyn)

- **Условие `#if`/`#elif`** (грамматика, прецеденты снизу вверх):
  `or := and ('||' and)*`; `and := eq ('&&' eq)*`; `eq := unary (('=='|'!=') unary)*`;
  `unary := '!' unary | primary`; `primary := '(' or ')' | ident | 'true' | 'false'`.
  **Нет `defined(X)`** (в отличие от C): голый `ident` = «определён ли символ».
- **`IsDefined(id)`**: сначала `DirectiveStack` (последний `#define`/`#undef` сверху вниз);
  если не задан в исходнике — падает на `Options.PreprocessorSymbols` (командная строка).
- **Порядок**: stack обновляется строго сверху вниз; `#define` **до** `#if` влияет на этот
  `#if`; `#define` в **не взятой** ветке — **не** влияет (skip-back к matching `#if`).
- **`BranchTaken`**: `#if`: `isActive && cond`; `#elif`: `endIsActive && cond && !prevTaken`;
  `#else`: `endIsActive && !prevTaken`. `prevTaken` = взята ли уже какая-то ветка ближайшего
  `#if` (только первая истинная ветва активна).
- **`#endif`** (`CompleteIf`): разворачивает stack до matching `#if`, оставляя директивы только
  из взятых секций.
- **Неактивная область**: `#define`/`#undef`/`#error`/`#warning`/`#line`/`#pragma` в ней —
  **без эффекта**; вложенные `#if` — не активны.
- **`#region`/`#endregion`** — не `Branching`, не влияют на active/inactive (только folding).

## Архитектура в CsNitra

- **Проект** `Parsers/CSharp/CsPreprocessor` (netstandard2.0), ссылки на `ExtensibleParser` +
  `CSharpGrammar`; добавить в `Nitra.sln`:
  - `Preprocessor.grammar` — текст CsNitra (структура файла + директивы + условие).
  - `PreprocessorTerminals.cs` — терминалы (т.ч. hand-written `CodeLine`).
  - `Preprocessor.cs` — точка входа: `PreprocessResult Run(string source, IEnumerable<string> commandLineSymbols)`.
  - `PreprocessorInterpreter.cs` — **visitor** (`ISyntaxVisitor`): на лету символы +
    active/inactive + сборка `Text` + диагностика.
  - `PreprocessResult.cs`, `Diagnostic.cs`, `LineDirective.cs` — данные (records).
- **Контракт `Run`**:
  1. Собрать `Parser` из `Preprocessor.grammar` (паттерн `CSharpParser.Build`).
  2. `Parse(source)` → parse tree, **плитка всего исходника** (top-узел `[0, source.Length)`).
  3. `PreprocessorInterpreter` обходит дерево в порядке исходника → `Text` + `Diagnostics` +
     `LineDirectives`.
- **Интеграция с C#-парсером**:
  `var pp = Preprocessor.Run(raw, symbols); var tree = CSharpParser.Parse(pp.Text);`
  — `pp.Text` парсится как обычный C# (директив в нём нет).

## Грамматика (черновик)

```
PreprocessorFile = Line*
Line             = DirectiveLine | CodeLine
DirectiveLine    = <ws>* '#' Directive <newline>
Directive        = If | Elif | Else | EndIf | Define | Undef | Error | Warning
                 | LineDir | Region | EndRegion | Pragma | Nullable | Shebang | Unknown
If               = 'if'   '(' Condition ')'
Elif             = 'elif' '(' Condition ')'
Else             = 'else'
EndIf            = 'endif'
Define           = 'define' Symbol
Undef            = 'undef'  Symbol
Error            = 'error'  Message
Warning          = 'warning' Message
LineDir          = 'line' LineTarget        // N "file" | default | hidden | (l,c)-(l2,c2) "file"
Region           = 'region'  Text
EndRegion        = 'endregion'
Pragma           = 'pragma' PragmaBody
Nullable         = 'nullable' NullableBody
Shebang          = '!' Text
Condition        = <ident, '!', '&&', '||', '==', '!=', '(', ')', 'true', 'false'>  // TDOPP
Symbol           = <идентификатор>
CodeLine         = <hand-written Terminal: строка, не начинающаяся (после ws) с '#'>
```

Замечания:
- В языке CsNitra **нет char-классов** → `CodeLine` = hand-written `Terminal` (сканер/DFA) —
  допустимо (CSharpParserPlan: hand-written только там, где декларативно невозможно).
- `Condition` — TDOPP/рекурсия (операторы `!`/`&&`/`||`/`==`/`!=`).
- **Trivia**: препроцессорный grammar строится построчно; проверить, что post-terminal
  trivia-skip в `Parser.cs` не ломает построчную плитку (риск ниже).

## План реализации

### Этап 0 — Инфраструктура

| # | Задача | Результат |
|---|--------|-----------|
| T0.1 | Проект `Parsers/CSharp/CsPreprocessor` (netstandard2.0), ссылки `ExtensibleParser` + `CSharpGrammar`; в `Nitra.sln`. Типы `PreprocessResult`/`Diagnostic`/`LineDirective` (records). `Preprocessor.Run` (заглушка: возвращает исходник как есть, без диагностик). | Собирается; `Run` существует |
| T0.2 | Тестовый проект `Tests/CsPreprocessorTests` (MSTest, net8.0), ссылка на CsPreprocessor; `[assembly: Parallelize(MethodLevel)]`. | Зелёный smoke-тест |

### Этап 1 — Грамматика препроцессора (CsNitra)

| # | Задача | Результат |
|---|--------|-----------|
| T1.1 | `Preprocessor.grammar`: `PreprocessorFile = Line*`, `Line = DirectiveLine \| CodeLine`; hand-written `CodeLine`. Грамматика парсит файл со смесью кода и директивных строк; top-узел покрывает весь текст. | Смешанный файл парсится, плитка `[0, len)` |
| T1.2 | Грамматика ДИРЕКТИВ: каждый вид (`if/elif/else/endif/define/undef/error/warning/line/region/endregion/pragma/nullable/shebang`/unknown) → узел с правильным `Kind` + детьми (symbol, message, line target). | Каждая директива → узел нужного `Kind` |
| T1.3 | Грамматика УСЛОВИЯ `#if`/`#elif`: `Condition` (ident, `!`, `&&`, `||`, `==`, `!=`, `(`, `)`, `true`, `false`) — TDOPP/рекурсия. | Условия → expression-узлы |

### Этап 2 — Интерпретатор (visitor)

| # | Задача | Результат |
|---|--------|-----------|
| T2.1 | `DirectiveStack` (чистая логика, без дерева): состояние символов (`#define`/`#undef`, только активные), `IsDefined` (stack + cmdline), `BranchTaken` (первая истинная ветка), `CompleteIf` на `#endif`. Как в D4. | Символы/ветки считаются верно (тесты в изоляции) |
| T2.2 | `PreprocessorInterpreter` (visitor): обход дерева в порядке исходника; active/inactive; сборка `Text` (same-length blanking: вычищать директивные строки + неактивный код, активный код как есть). `Text.Length == source.Length`, `\n`/`\r` сохраняются. | Чистый текст той же длины |
| T2.3 | Диагностика: `#error`/`#warning` (только активные), структурные ошибки (несовпадение `#if`/`#endif`/`#else`, bad placement). В координатах исходника. | Диагностика в исходных координатах |

### Этап 3 — Сохранение позиций (ОТДЕЛЬНЫЕ тесты)

| # | Задача | Результат |
|---|--------|-----------|
| T3.1 | Тесты сохранности позиций: для набора входов с директивами — `Text.Length == source.Length`; для каждого активного `i` `Text[i]==source[i]`; вычищенные `i` — пробел; позиции newline совпадают. | Позиции 1:1 доказаны |
| T3.2 | Интеграционный тест: препроцессор → CSharpParser на `Text`; намеренная ошибка в реальном файле → её позиция в `Text` == позиции в исходнике. | Диагностика PEG = позиции исходника |

### Этап 4 — Краевые случаи

| # | Задача | Результат |
|---|--------|-----------|
| T4.1 | Вложенные `#if`; `#else`/`#elif` без `#if`; незакрытый `#if` (EOF); `#endif` без `#if` — диагностика как в Roslyn. | Совпадает с Roslyn |
| T4.2 | `#define`/`#undef` в неактивной области — без эффекта; `#error`/`#warning` в неактивной — не срабатывают. | Как в Roslyn |
| T4.3 | `#line N "file"` / `#line default` / `#line hidden` — запоминание в `LineDirectives`. | `LineDirectives` заполнены |
| T4.4 | Multi-line string/comment с line-start `#` (в `/* */`, `"""`, `@"`) — НЕ директива. | Строки/комментарии не ломаются |
| T4.5 | Shebang `#!` (offset 0); CRLF vs LF; ws перед `#`; `#` после токена в строке (bad placement). | Корректно |

### Этап 5 — Закаливание

| # | Задача | Результат |
|---|--------|-----------|
| T5.1 | Прогон по реальным .cs-файлам (репо/Roslyn): препроцессор + CSharpParser, без краха, верный active/inactive. | Реальные файлы обрабатываются |
| T5.2 | (При необходимости) производительность на больших файлах. | Отчёт |

## Риски / ограничения

- **Char-классов нет** → `CodeLine` hand-written (T1.1). Если упрёмся — возможно, придётся
  добавить char-классы в язык CsNitra (пересекается с CSharpParserPlan T6.1).
- **Trivia-skip** (post-terminal в `Parser.cs`) может конфликтовать с построчной плиткой —
  проверить в T1.1; при необходимости trivia-терминал/отключение для препроцессорного grammar.
- **Multi-line string/comment** (T4.4) — самый сложный край; возможно, потребует отслеживания
  состояния строки/комментария в visitor (в active-регионе).
- **`#line` display remap** — только запоминание (D5); реальное отображение — вне скоупа
  первого прохода.

## Не в скоупе

- Отображение `#line` (только запись `LineDirectives`).
- `#pragma checksum`, `#r`, `#load` (scripts) — распознавать как unknown/no-op.
- Семантика за пределами препроцессора (binding, type-check).
