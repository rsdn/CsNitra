# T3.5.1 Progress — Дизайн: правила интерполяции (3 уровня $) → `docs/InterpolatedStringGrammar.md`

## Status: done (дизайн-документ написан; код не изменялся)

## Задача

Только дизайн (документ), без изменения кода. Написать `docs/InterpolatedStringGrammar.md`:
грамматические правила интерполированных строк C# для CsNitra. 3 явных уровня по числу `$`
(D=1, D=2, D=3) вместо общего контекстно-зависимого правила (оно придёт в Этапе 5).
Дыра = `{ Expression }` (рекурсия в реальное правило выражений).

## Прочитано (контекст)

- `docs/CSharpParserPlan.md` — разделы «Строковые литералы: архитектура» и «Этап 5»;
  решение пользователя (сканерная интерполяция отклонена; 3 явных варианта $/$$/$$$;
  общее — после Этапа 5).
- `docs/CSharpParserPlan-checklist.md` — статус, Deviations (решения пользователя: только
  Roslyn, 3 явных варианта, T3.5.1–T3.5.2 до остальных пунктов).
- `Parsers/CSharp/CSharpGrammar/StringLiteralScanner.cs` — текущий (заменяемый) сканер:
  `TryScanInterpolatedString` (regular/verbatim, D=1), `TryScanRawInterpolatedString`
  (raw, D≥1), `TryScanStringContents` (content-loop, raw-ветки brace-runs), `TryScanHoleBalancedText`,
  `TryScanFormatSpecifier`.
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` — терминалы `InterpolatedStringLiteral`,
  `RawInterpolatedStringLiteral` (+ `StringLiteral`, `VerbatimStringLiteral`, `RawStringLiteral`
  остаются).
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — текущая грамматика (правила `Expression` НЕТ;
  `Constant` ссылается на `StringLiteral`/`VerbatimStringLiteral`).
- `Parsers/CSharp/CSharpGrammar/CSharpParser.cs` — API слияния
  `CSharpParser(IReadOnlyList<(Text,Path)>, trivia, terminals)`; `BuildTdoppRules()` вызывается.
- `Tests/CSharpGrammarTests/*` — паттерны тестов: `EmbeddedGrammar.LoadCs1Grammar()`,
  `parser.Parse(input, startRule, out triviaLength)` (startRule = любое правило),
  `CSharpParserMultiTextTests` (слияние: повторное имя = добавление альтернатив).
- `Parsers/CsNitra/CsNitraGrammar/CsNitraParser.cs` + `RuleGenerator.cs` + `Ast/*.cs` —
  язык текстовых грамматик: Seq/Alt/Ref/Literal/OneOrMany/ZeroOrMany/Optional/`&`/`!`/
  SeparatedList/TDOPP `ReqRef` (`RuleRef : PrecedenceName [, right]`); `precedence`-statement
  (первое имя = максимальный binding power, последнее = минимальный).
- `ExtensibleParser/Rules.cs` + `Parser.cs` + `Parser.NoRecovery.cs` — движок: longest-match
  среди альтернатив (при равной длине Success > Partial; тай-брейк по первому), TDOPP
  (prefix = не `Seq([Ref(self),..])`, postfix = `Seq([Ref(self),..])` с `ReqRef`),
  `ZeroOrMany`/`OneOrMany` (пер-элемент longest-match, стоп на нулевом прогрессе).
- `docs/CSharpParserPlan-progressT1.2.5.md` — готовый Roslyn-ресёрч raw-строк (таблица
  brace-runs, CS9002/9005/9006/9007, format, nesting) — базис для раздела 1.

## Roslyn (обязательный источник; checkout `C:\RSDN\roslyn`, без веба)

- `src/Compilers/CSharp/Portable/Parser/Lexer.cs:736-778` — `TryScanAtStringToken`
  (`@`-ран → `"` = verbatim, `$` = interpolated), `TryScanInterpolatedString`
  (`$` + `$`/`@`/`"` → `ScanInterpolatedStringLiteral`).
- `src/Compilers/CSharp/Portable/Parser/Lexer_StringLiteral.cs` — вся логика:
  - `ScanOpenQuote:415-520` — dispatch: `($,@,")`/`(@,$,")` → Verbatim D=1 N=1;
    `($,",not ")`/`($,"","not ")` → Normal D=1 N=1 (НЕ `$"""`); иначе raw-путь
    (D=`$`-ран, N=`"`-ран; N<3 → CS1005; `@` → CS8999/`ERR_IllegalAtSequence`).
    **Вывод: regular/verbatim — ровно один `$`; D>1 только в raw.**
  - `ScanInterpolatedStringLiteralContents:653-717` — content-loop (`,`/`}`/`{`/`\`).
  - `HandleOpenBraceInNormalOrVerbatimContent:867-894` — `{{` = escape, `{` = дыра.
  - `HandleCloseBraceInContent:818-853` — normal/verbatim: `}}` = escape, одиночная `}` =
    CS1052 `ERR_UnescapedCurly`; raw: `}`-ран M≥D = CS9007.
  - `HandleOpenBraceInRawContent:896-957` — raw: `{`-ран K: K<D = content; K≥2D = CS9006;
    D≤K<2D = дыра (K−D литер. + D откр.); close-ран M: 0=CS1054, <D=CS9005, ≥D = дыра
    (D потреблено, остаток → content, где ≥D = CS9007).
  - `ScanFormatSpecifier:959-1017` — формат = `:` + символы до `}`; норм. — `\`-экраны
    (`\{`/`\}` = CS1053 `ERR_EscapedCurly`), verbatim — `""`, raw — без экранов.
    **Формат запускает ПЕРВОЕ верхнеуровневое `:` (не в скобках/строках) — не запятая.**
  - `ScanInterpolatedStringLiteralHoleBalancedText:1022-1141` — тело дыры: newlines всегда,
    вложенные строки/`$`-интерполяции/`@`-строки, `#` = ошибка, баланс `()`/`[]`/`{}`,
    первое верхнеуровневое `:` → формат.
- `src/Compilers/CSharp/Portable/Parser/Lexer_RawStringLiteral.cs:11-46` — `Consume*Sequence`
  (раны `"`/`$`/`@`/`{`/`}`), `ConsumeWhitespace`.
- Тесты-подтверждения: `Test/Syntax/Parsing/RawInterpolatedStringLiteralParsingTests.cs`
  (brace-runs, format, nesting), `InterpolatedStringExpressionTests.cs`.

## Ключевые решения (приняты)

1. **3 уровня по D (числу `$`)**: D=1 (regular `$"`, verbatim `$@`/`@$`, raw `$"""`),
   D=2 (raw `$$"""`), D=3 (raw `$$$"""`). Regular/verbatim — только D=1 (Roslyn `ScanOpenQuote`).
2. **Дыра = D фигурных скобок**: D=1 `{ E }`, D=2 `{{ E }}`, D=3 `{{{ E }}}`.
3. **Raw brace-runs через negative-lookahead `!`** (текущий язык не умеет вычисленные
   повторения/`Count()`): литеральный запуск скобок длины <D — с `!`-предикатом «не длиннее»;
   дыра — запуски D..2D−1 (с литер. префиксом 0..D−1); запуск ≥2D — нет альтернативы → ошибка.
   Longest-match + `!` даёт корректное разбиение без равных длин.
4. **Формат = `:` + текст до `}`** (не запятая, не выражение — по Roslyn `ScanFormatSpecifier`).
   Дыра = `{` Expression Format? `}`.
5. **Временное `Expression` в Cs1.grammar** (выражения — C# 1.0), TDOPP через `precedence` +
   `ReqRef`; помечено как временное (T2.3 заменит). Включает литералы/идентификаторы/
   member/index/invocation/унарные/бинарные/условное/присваивание/запятую.
6. **Организация файлов**: правила интерполяции (CS6) → новый `Cs6.grammar`; временное
   `Expression` (CS1.0) → `Cs1.grammar`; слияние Cs1+Cs6 через существующий API
   (T0.3). Version-purity (T1.3.1) соблюдена. Интерполяция — НЕ константа: альтернативы
   добавляются в `Expression` (не в `Constant`).
7. **Удаление в T3.5.2**: терминалы `InterpolatedStringLiteral`, `RawInterpolatedStringLiteral`
   + сканерные `TryScanInterpolatedString`/`TryScanAtInterpolatedString`/`TryScanRawInterpolatedString`
   + hole-скан (`TryScanHoleBalancedText` и raw-ветки brace/format). Остаются: `StringLiteral`,
   `VerbatimStringLiteral`, `RawStringLiteral` (не-интерполированные) + их сканерные пути.

## Открытые вопросы / ограничения (задокументировать, не решать)

- **Формат vs условное `?:`**: в реальном C# первое верхнеуровневое `:` в дыре = формат,
  поэтому верхнеуровневый `?:` в дыре НЕ разрешён (Roslyn: expr до `:`, формат после →
  `a ? b : c` в дыре = ошибка). Грамматический подход с полным TDOPP `Expression` может
  разобрать `{a ? b : c}` как условие (расхождение). Задокументировать; решить в T2.3.
- **Raw 4+ кавычки** — отложено (план, «Риски»); дизайн — только N=3.
- **Общее N-$** — Этап 5 (параметризуемые правила, `Count()`).
- **D1** (epsilon на start rule) — взаимодействие с фрагментным парсингом (start rule ≠ `Grammar`).
- **Multi-line raw** — самая сложная часть (CS9002 пустое тело, close-line, mid-line delimiter);
  структура задана, детали — в документе.

## Что осталось

- [x] Прочитать контекст + Roslyn
- [x] Принять ключевые решения
- [x] Написать `docs/InterpolatedStringGrammar.md` (разделы 1–7 + приложения A/B)
- [x] Финальная сверка с longest-match движком + TDOPP
- [x] Верификация оркестратора + исправление критической ошибки trivia
      (§2.2/§2.3/§2.5–2.7/§6.3/§7)

## Верификация оркестратора + исправления

Оркестратор проверил документ по коду. Семантика Roslyn (§1, §2.1, §2.8, §3, §4, §5)
подтверждена — без изменений. Найден **критический** дефект в исходном дизайне raw-веток:

**Ошибка**: исходный дизайн предполагал, что trivia «не съест» whitespace/newlines внутри
строки. На деле движок пропускает trailing trivia после **каждого** терминала
(`Parser.cs:686-689`, `ParseTerminal`), а `Literal` — это `Terminal` (`Rules.cs:65`),
`TriviaTerminal` (`CSharpTerminals.cs:144-209`) матчит ЛЮБЫЙ whitespace (вкл. newlines).
Следствие: все ws/newlines между терминалами внутри строки **съедаются trivia** →
`RawOpenNewline`/`RawContentLine`/`RawCloseLine`/`Newline`/`RawWs` — мёртвые правила,
ветка MultiLine/SingleLine — лишняя.

**Исправления** (в `docs/InterpolatedStringGrammar.md`):
- §2.2 — терминалы: text-раны **жадные** (newline допустим); удалены `Newline`/`RawWs`;
  добавлено примечание о trivia + синтаксис литералов (`"""` → `"\"\"\""`).
- §2.3 — примечание: `InterpolatedRegularText` жадный, newline допустим (расхождение §7.5).
- §2.5/§2.6/§2.7 — raw: **один контент-цикл** `RawPart*`; удалены MultiLine/SingleLine,
  `RawOpenNewline`/`RawContentLine`/`RawCloseLine`, guard `!'"'` после открывающих `"""`
  (давал ложное отклонение валидного `$""" """`); литерал `""""` → `"\"\"\""`.
  Цена: `$""""""` (6 кавычек) и пустое multi-line тело `$"""\n"""` теперь принимаются
  (расхождения, §7.5).
- §6.3 — новый раздел: тесты-документации newline-прозрачности (4 расхождения, grammar
  принимает / Roslyn отклоняет). Из §6.2 невалидных убраны 2 кейса (regular newline,
  пустое multi-line) — они теперь принимаются.
- §7 — переделан в подпункты 7.1–7.6; новый **§7.5** — newline-прозрачность: 4 расхождения
  с точными кодами Roslyn (CS1039/CS8997/CS9002/CS9000,
  `Lexer_StringLiteral.cs:556/581/732/628`) + «валидный код не затронут».

Коды ошибок сверены с исходником Roslyn (`ErrorCode.cs`): regular unterminated = **CS1039**
(не CS1010), raw delimiter-on-own-line = **CS9000** (не CS8999).

Код не изменялся (только документ + этот progress-файл).

## Итог

`docs/InterpolatedStringGrammar.md` — 670 строк, все 7 обязательных разделов + приложения.
Код не изменялся (только документ + этот progress-файл).

Решения (кратко):
- 3 уровня по D: D=1 (regular/verbatim/raw), D=2 (raw), D=3 (raw); regular/verbatim —
  только D=1 (Roslyn `ScanOpenQuote`).
- Дыра = D фигурных скобок; raw brace-runs через `!`-lookahead (запуск ≥D = дыра, иначе
  литерал; без вычисленных повторений).
- Формат = `:` + текст до `}` (не запятая, не выражение).
- Временное `Expression` (TDOPP) в Cs1.grammar; интерполяция (CS6) в новом Cs6.grammar;
  слияние Cs1+Cs6 (T0.3); интерполяция → альтернативы `Expression` (не `Constant`).
- Удаление в T3.5.2: терминалы `InterpolatedStringLiteral`/`RawInterpolatedStringLiteral`
  + hole-скан; остаются plain/verbatim/raw (без $).

Открытые вопросы (задокументированы в §7.1–7.6, не решены): raw 4+ кавычки (отложено, §7.1),
общее N-$ (Этап 5, §7.2), формат vs `?:` (расхождение, T2.3, §7.3), D1 (epsilon на start
rule, §7.4), **newline-прозрачность** (4 расхождения, grammar более permisсивна, §7.5),
временное `Expression` (минимальный набор, §7.6).
