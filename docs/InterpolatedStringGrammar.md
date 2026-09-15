# Интерполированные строки C# — грамматика CsNitra (дизайн T3.5.1)

Дизайн грамматических правил интерполированных строк для парсера CsNitra. Заменяет
сканерную реализацию (`InterpolatedStringLiteral` / `RawInterpolatedStringLiteral` в
`CSharpTerminals.cs` + hole-скан в `StringLiteralScanner.cs`) — см. решение пользователя в
`CSharpParserPlan.md` («Строковые литералы: архитектура») и Deviations checklist.

**Область**: 3 явных уровня по числу `$` (D=1, D=2, D=3). Общее (произвольное N-$) — **Этап 5**
(параметризуемые правила, `Count()`); здесь **не** выразимо и **не** проектируется.

**Дыра** = `{ Expression }` — рекурсия в реальное правило выражений (временное `Expression`
спроектировано в §3, T2.3 заменит на полную TDOPP-таблицу).

**Выразимость**: все правила ниже — в ТЕКУЩЕМ языке текстовых грамматик CsNitra
(`Seq`/`Alt`/`Ref`/`Literal`/`OneOrMany`/`ZeroOrMany`/`Optional`/`&`/`!`/`SeparatedList`/TDOPP
`ReqRef`). Параметризуемых правил, `Count()`, вычисленных повторений **нет**.

---

## 1. Семантика по данным Roslyn

Источник: `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\` (только код, без веба).
Ключевые файлы: `Lexer.cs`, `Lexer_StringLiteral.cs`, `Lexer_RawStringLiteral.cs`.
Готовый ресёрч raw-веток — `docs/CSharpParserPlan-progressT1.2.5.md` (таблица brace-runs).

### 1.1. Валидные комбинации (семейство × число `$`)

Dispatch входа — `Lexer.cs:736-778` (`TryScanAtStringToken`, `TryScanInterpolatedString`) и
`Lexer_StringLiteral.cs:415-520` (`ScanOpenQuote`):

| Форма | `$`-ран D | `"`-ран N | Семейство | Условие в `ScanOpenQuote` |
|---|---|---|---|---|
| `$"..."` | 1 | 1 | regular (Normal) | `('$','"',not '"',_)` / `('$','"','"',not '"')` — `:438-449` (**не** `$"""`) |
| `$@"..."` / `@$"..."` | 1 | 1 | verbatim (Verbatim) | `('$','@','"')` / `('@','$','"')` — `:424-436` |
| `$"""..."""` и др. | D≥1 | N≥3 | raw (Single/MultiLineRaw) | общий путь `:451-519`: D=`$`-ран, N=`"`-ран |

**Вывод (проверено в коде)**:
- **regular и verbatim — ровно ОДИН `$`** (D=1). `$$"..."` не является regular/verbatim:
  `ScanOpenQuote` не матчит `('$','$',...)` в Normal/Verbatim, уходит в raw-путь с N=1 < 3 →
  `ERR_NotEnoughQuotesForRawString` (CS1005). Т.е. несколько `$` в regular/verbatim **невозможно**.
- **raw — D≥1 и N≥3**. N<3 → CS1005; `@` в префиксе raw → `ERR_IllegalAtSequence`.

Следовательно, валидные (семейство × уровень $):

| Уровень D | regular | verbatim | raw |
|---|---|---|---|
| D=1 | `$"..."` | `$@"..."/@$"..."` | `$"""..."""` |
| D=2 | — | — | `$$"""..."""` |
| D=3 | — | — | `$$$"""..."""` |

Итого **5 семейств правил**: regular-1, verbatim-1, raw-1, raw-2, raw-3.

### 1.2. Дыра: открывание/закрывание, «избыточные» скобки

Дыра открывается/закрывается **D фигурными скобками** (D = число `$`).

**Regular/Verbatim (D=1)** — `Lexer_StringLiteral.cs`:
- Открытие `HandleOpenBraceInNormalOrVerbatimContent:867-894`: `{{` → **escape** (литер. `{`,
  потребляет 2); одиночный `{` → **дыра** (тело = `ScanInterpolatedStringLiteralHoleBalancedText`).
- Закрытие `HandleCloseBraceInContent:818-834` (normal/verbatim): `}}` → **escape** (литер. `}`);
  одиночная `}` → **ошибка** `ERR_UnescapedCurly` (CS1052).
- Т.е. в regular/verbatim: `{{`/`}}` — экранирование, `{`/`}` — дыра/ошибка.

**Raw (D≥1)** — `HandleOpenBraceInRawContent:896-957`, `HandleCloseBraceInContent:835-852`.
Запуск скобок = максимальный непрерывный ран `{` (или `}`).

Открывающий запуск K (в контенте):
- **K < D** → литеральный текст (K скобок).
- **D ≤ K < 2D** → **дыра**: первые K−D скобок — литер. текст, последние D открывают дыру
  (комментарий `:900-904`: «up to 2*N−1 ... the inner N braces start the interpolation»).
- **K ≥ 2D** → **ошибка** `ERR_TooManyOpenBracesForRawString` (CS9006) на первых K−D скобках
  (дыра всё равно стартует с последних D).

Закрывающий запуск M:
- В контенте (`HandleCloseBraceInContent:835-852`): **M < D** → литер. текст; **M ≥ D** →
  **ошибка** `ERR_TooManyCloseBracesForRawString` (CS9007).
- Закрытие дыры (`:927-951`): после тела дыры `}`-ран M: **M==0** → CS1054
  `ERR_UnclosedExpressionHole`; **0<M<D** → CS9005 `ERR_NotEnoughCloseBracesForRawString`;
  **M ≥ D** → потребляется ровно D, остаток M−D возвращается в контент (где ран ≥ D = CS9007).
  Итог: корректное закрытие дыры = **D ≤ M < 2D**; M ≥ 2D → ошибка.

Сводная таблица (D = число `$`):

| Запуск в контенте | D=1 | D=2 | D=3 | Общий |
|---|---|---|---|---|
| `{`-ран K < D | (нет, K=0) | `{` | `{`, `{{` | литер. текст |
| `{`-ран D ≤ K < 2D | `{` (дыра) | `{{`, `{{{` | `{{{`..`{{{{{` | дыра (K−D литер. + D откр.) |
| `{`-ран K ≥ 2D | `{{` | `{{{{` | `{{{{{{` | **ошибка CS9006** |
| `}`-ран M < D (контент) | (нет) | `}` | `}`, `}}` | литер. текст |
| `}`-ран M ≥ D (контент) | `}` | `}}` | `}}}` | **ошибка CS9007** |
| закрывающий ран дыры M | D ≤ M < 2D | D ≤ M < 2D | D ≤ M < 2D | иначе ошибка |

**Ключевой факт для D=1 raw**: одиночный `{` — всегда дыра, `{{` — **ошибка** (не escape!).
Литеральный `{` в raw невозможен при D=1 (нужен D≥2 и одиночный `{` в контенте).
Подтверждено: `RawInterpolatedStringLiteralParsingTests.cs:156-171` (`$"""{{0}}"` → CS9006).

### 1.3. Экранирование кавычек и обратных слэшей

- **Regular**: `\` — escape-последовательность (`ScanEscapeSequence`, `Lexer.cs:129-190`):
  `\"  \'  \\  \0  \a  \b  \f  \n  \r  \t  \v  \x…  \u…  \U…`. **`\{` и `\}` — ошибка**
  `ERR_EscapedCurly` (CS1053) — `Lexer_StringLiteral.cs:697-702` (и в формате `:982-985`).
  Одиночная `"` закрывает строку.
- **Verbatim**: `\` — **обычный контент** (без экранов, `:695-707` raw-ветка else). `""` —
  escape (литер. `"`), одиночная `"` закрывает (`IsEndDelimiterOtherwiseConsume:788-796`).
- **Raw**: **никаких экранирований**. `\` — контент; `"`-ран < N — контент, `"`-ран ≥ N —
  закрытие (весь ран потребляется; избыток кавычек = CS8998, но ран входит в токен).

### 1.4. Format-спецификатор

`ScanFormatSpecifier:959-1017` + комментарий со спекой `:961-970`:

```
interpolation_format           : ':' interpolation_format_character+
interpolation_format_character : <любой символ кроме " : { }>
```

Запуск — **ПЕРВОЕ верхнеуровневое `:`** (не вложенное в `()`/`[]`/`{}` и не в строке) —
`ScanInterpolatedStringLiteralHoleBalancedText:1054-1062`. **Не запятая** (в отличие от
`string.Format`): в интерполяции C# формат = `{ expr : format }`.

После `:` — символы до `}`:
- `}` → конец формата/дыры.
- `"`: normal — одиночная `"` = преждевременный конец (ошибка у вызывающего); verbatim — `""`
  = escape; raw — `"` = контент.
- `\`: normal — escape (`\{`/`\}` = CS1053); verbatim/raw — контент.
- `{`: ошибка `ERR_UnexpectedCharacter`, но скан продолжается.
- `:` — **не** останавливает (код просто пропускает; расхождение с комментарием спеки —
  код авторитетен).

Формат — **строка символов, НЕ выражение** (в старом шаблоне `CS6Literals.nitra` было
`Format=Expression` и запятая — оба неверны по Roslyn).

### 1.5. Тело дыры (общее для всех семейств/уровней)

`ScanInterpolatedStringLiteralHoleBalancedText:1022-1141`. Внутри дыры — **полный C# той же
версии** (рекурсия):
- newlines всегда разрешены (ограничение на newline — только в текстовой части строки).
- `"` / `'` → вложенный строковый/char-литерал (`ScanInterpolatedStringLiteralNestedString` →
  `ScanStringLiteral`, который сам диспатчит 3+-кавычный raw).
- `$` → вложенная интерполяция (`TryScanInterpolatedString`, который сам диспатчит `$$`-raw).
- `@` → `TryScanAtStringToken` (verbatim/interpolated).
- `#` → ошибка (препроцессор в дыре запрещён).
- `{`/`(`/`[` → балансировка скобок (`ScanInterpolatedStringLiteralHoleBracketed`);
  `}`/`)`/`]` → конец, если совпадает с ожидаемым.
- первое верхнеуровневое `:` → формат (см. 1.4).

### 1.6. Ошибки (сводка)

| Условие | Код | Где |
|---|---|---|
| одиночная `}` в тексте regular/verbatim | CS1052 `ERR_UnescapedCurly` | `:832` |
| незакрытая дыра (`{` без `}`) | CS1054 `ERR_UnclosedExpressionHole` | `:886`, `:933` |
| `}`-ран дыры 0 < M < D (raw) | CS9005 `ERR_NotEnoughCloseBracesForRawString` | `:941` |
| `{`-ран ≥ 2D (raw) | CS9006 `ERR_TooManyOpenBracesForRawString` | `:918` |
| `}`-ран ≥ D в контенте (raw) | CS9007 `ERR_TooManyCloseBracesForRawString` | `:847` |
| `\{` / `\}` (regular) | CS1053 `ERR_EscapedCurly` | `:701`, `:984` |
| незакрытая строка (newline/EOF до close) | CS8997 `ERR_UnterminatedStringLit` / `ERR_UnterminatedRawString` | `:556`, `:581` |
| пустое тело multi-line raw | CS9002 `ERR_RawStringMustContainContent` | `:731` |
| `<3` кавычки после `$` (raw-префикс) | CS1005 `ERR_NotEnoughQuotesForRawString` | `:497` |

**Конвенция парсера (fail-fast, по T1.2.3/T1.2.4)**: грамматическое правило **падает**
(нет совпадения) на этих условиях → фрагментный парсинг отклоняет ввод. Исключение —
избыток закрывающих кавычек (CS8998) и delimiter в середине строки (CS8999/CS9000): там
расширение токена определено, но в грамматическом подходе это выливается в «лишний» ввод
после строки → фрагмент `end != input.Length` → тоже отклоняется (см. §7).

---

## 2. Грамматические правила (текст CsNitra, готов к вставке)

### 2.1. Механика longest-match и почему `!`-предикаты обязательны

Движок (`Parser.cs:246-310`) при каждом шаге пробует **все** альтернативы и берёт
**самое длинное** совпадение; при равной длине `Success` > `Partial`, иначе — первая.
AGENTS.md: «при равенстве длин — ошибка». Поэтому альтернативы проектируются так, чтобы
**не было равных длин** на одной позиции:

1. **Литеральный запуск скобок/кавычек** матчится только если он **короче** порога (D для
   скобок, N=3 для кавычек) — через `!`-negation следующего того же символа:
   `"{" !"{"` = ровно одна `{` (не запуск длиннее).
2. **Дыра** матчится запуски D..2D−1 (с литер. префиксом 0..D−1) и **требует** валидного
   `Expression` + закрывающего запуска. Если дыра не закрывается — альтернатива падает,
   и (поскольку литеральный запуск ≥ D не существует) позиция не матчится → **ошибка**
   (совпадает с CS9005/9006/1054).
3. **Текстовый ран** (`*Text`-терминалы) **исключает** `{`, `}`, `"` (и `\` в regular) →
   не пересекается с дырами/escape/кавычками.

Результат: разбиение «запуск скобок длины ≥ D = дыра, иначе текст» достигается без
вычисленных повторений — только литералами заданной длины + `!`-lookahead.

### 2.2. Терминалы (спецификация для T3.5.2)

Язык текста грамматики не имеет char-классов/regex — «раны безопасных символов» и
escape/формат — это **hand-written терминалы**. Спецификация (реализация — T3.5.2; можно
переиспользовать логику `StringLiteralScanner.TryScanEscape`):

| Терминал | Матчит | Где |
|---|---|---|
| `InterpolatedRegularText` | **жадный** ран `[^\{\}\\"]+` (newline **допустим**) — до `{`/`}`/`\`/`"` | regular контент |
| `InterpolatedRegularEscape` | `\` + валидный escape-char (`\"\'\\0abfnrtv` / `\x…` / `\u…` / `\U…`); `\{`,`\}` и невалидные → **не матчит** (ошибка) | regular контент |
| `InterpolatedVerbatimText` | **жадный** ран `[^\{\}"]+` (newline **допустим**) — до `{`/`}`/`"` | verbatim контент |
| `InterpolatedRawText` | **жадный** ран `[^\{\}"]+` (newline **допустим**) — до `{`/`}`/`"` | raw контент (все D) |
| `RegularFormatText` | ран до `}`: `\`-экраны, одиночная `"` = преждевременный конец; `{` пропускается | regular формат |
| `VerbatimFormatText` | ран до `}`: `""` = escape, одиночная `"` = преждевременный конец | verbatim формат |
| `RawFormatText` | ран до `}` (без экранов) | raw формат |

> **КРИТИЧНО — trivia внутри строки.** Движок пропускает trailing trivia после **каждого**
> терминала (`Parser.cs:686-689`, `ParseTerminal`), а `Literal` — это `Terminal`
> (`Rules.cs:65`), поэтому литералы `"""`, `"$"`, `"{"` и т.д. тоже идут через
> `ParseTerminal`. `TriviaTerminal` (`CSharpTerminals.cs:144-209`) матчит ЛЮБЫЙ whitespace
> (`char.IsWhiteSpace`, включая newlines) + комментарии. Следствие: **все whitespace/newlines
> между терминалами внутри строки съедаются trivia-сканером** до матчинга следующего элемента
> грамматики. Поэтому:
> - text-раны заданы **жадными** (newline допустим) — ран сам поглощает whitespace/newlines,
>   и после него trivia-сканеру нечего есть (следующий символ — `{`/`}`/`"`/`\`/EOF).
> - structural newline после открывающих кавычек тоже съедается trivia — совпадает с Roslyn
>   (open-quote section `ws + newline` не входит в контент, `ScanOpenQuote:500-517`).
> - Отдельные терминалы `Newline`/`RawWs` **не нужны** (мёртвые: newline после литерала уже
>   съеден trivia, они бы никогда не матчились) — удалены.
> - Цена: newline становится «прозрачным» внутри строки → грамматика **более permisсивна**,
>   чем Roslyn (принимает невалидный код) — см. §7.5. Локальными альтернативами это
>   **неисправимо** (trivia-сканер глобален и не знает, что он внутри строки).
>
> **Синтаксис литералов в файле `.grammar`**: кавычки внутри литерала экранируются `\`.
> Литерал 3-символьной строки `"""` пишется как `"\"\"\""`; `"$"` → `"$"`, `"$`×2` → `"$$"`,
> `"$`×3` → `"$$$"`, `":"` → `":"`, `"{"` → `"{"`, `"}"` → `"}"`, `","` → `","`.

### 2.3. D=1 — regular `$"..."`

```
InterpolatedStringLiteral = "$\"" InterpolatedRegularPart* "\"";

InterpolatedRegularPart =
    | Hole = InterpolatedHole1
    | OpenBraceEscape = "{{"
    | CloseBraceEscape = "}}"
    | Escape = InterpolatedRegularEscape
    | Text = InterpolatedRegularText;

InterpolatedHole1 = "{" Expression RegularFormat? "}";
RegularFormat     = ":" RegularFormatText;
```

Разбор longest-match на позиции `{`:
- `{{` → `OpenBraceEscape` (2 зн.); `Hole` = `{`+`Expression` падает (2-й `{` — не начало
  `Expression`) → escape выигрывает.
- `{expr}` → `Hole` (длинный) выигрывает; `OpenBraceEscape` падает (одна `{`).
- одиночный незакрытый `{` → `Hole` падает, `OpenBraceEscape` падает, `Text` не стартует с
  `{` → контент-цикл стоп, закрывающая `"` не матчится → **ошибка** (CS1054).
- одиночная `}` → `CloseBraceEscape` падает, других альтернатив нет → **ошибка** (CS1052).

`InterpolatedRegularText` — **жадный** ран `[^\{\}\\"]+` с **допустимым** newline (см. §2.2):
ран сам поглощает whitespace/newlines, и trivia-сканеру после него нечего есть. Newline в
regular-тексте Roslyn отклоняет (CS1039), а grammar принимает — расхождение newline-прозрачности
(§7.5); приём/отказ на валидном коде совпадает.

### 2.4. D=1 — verbatim `$@"..."` / `@$"..."`

```
VerbatimInterpolatedStringLiteral = ( "$@\"" | "@$\"" ) VerbatimInterpolatedPart* "\"";

VerbatimInterpolatedPart =
    | Hole = VerbatimHole1
    | OpenBraceEscape = "{{"
    | CloseBraceEscape = "}}"
    | QuoteEscape = "\"" "\""
    | Text = InterpolatedVerbatimText;

VerbatimHole1 = "{" Expression VerbatimFormat? "}";
VerbatimFormat = ":" VerbatimFormatText;
```

Отличия от regular: **нет** `\`-escape (`\` — часть `InterpolatedVerbatimText`); `""` —
escape; newline в тексте разрешён. `"""` в контенте = `QuoteEscape`(`""`) + закрывающая `"`
(строка кончается) — совпадает с Roslyn.

### 2.5. D=1 — raw `$"""..."""`

```
RawInterpolatedStringLiteral = "$" "\"\"\"" RawPart1* "\"\"\"";

RawPart1 =
    | Hole = RawHole1
    | Quote1 = "\"" !'"'
    | Quote2 = "\"" "\"" !'"'
    | Text = InterpolatedRawText;

RawHole1 = "{" Expression RawFormat? "}";
RawFormat = ":" RawFormatText;
```

Для D=1 raw **нет** литеральных скобочных альтернатив: одиночный `{` — дыра, `{{` — ошибка
(альтернативы нет → падение), `}` — ошибка (альтернативы нет).

Контент — **один цикл** `RawPart1*`. Newlines/whitespace между терминалами съедаются
глобальным trivia-сканером (§2.2), поэтому веток MultiLine/SingleLine и правил
`RawOpenNewline`/`RawContentLine`/`RawCloseLine` **нет**: newline после открывающих `"""`
уже съедён trivia до начала цикла, отдельное правило для него было бы мёртвым.

Старый guard `!'"'` после открывающих `"""` **удалён**: после литерала trivia съедает
whitespace, и следующий символ валидной single-line строки часто — закрывающие `"""`
(напр. `$""" """`), поэтому `!'"'` давал ложное отклонение валидного ввода. Цена:
`$""""""` (6 кавычек) теперь принимается как пустое тело — расхождение в рамках отложенного
N>3 (§7.1/§7.5). Пустое multi-line тело `$"""\n"""` теперь тоже **принимается** (trivia
съедает newline, цикл = 0 частей, закрывающие `"""` матчатся) — расхождение с Roslyn CS9002
(§7.5).

### 2.6. D=2 — raw `$$"""..."""`

```
RawInterpolatedStringLiteral2 = "$$" "\"\"\"" RawPart2* "\"\"\"";

RawPart2 =
    | HoleB = "{{{" Expression RawFormat? "}}"     // K=3: 1 литер. + дыра
    | HoleA = "{{"  Expression RawFormat? "}}"     // K=2: дыра
    | OpenBrace1 = "{" !"{"                         // K=1: литер.
    | CloseBrace1 = "}" !'}'                        // M=1: литер.
    | Quote1 = "\"" !'"'
    | Quote2 = "\"" "\"" !'"'
    | Text = InterpolatedRawText;
```

Разбор на позиции `{` (K = длина `{`-рана):
- K=1 → `OpenBrace1` (1 зн.); `HoleA`/`HoleB` падают (мало `{`).
- K=2 → `HoleA` = `{{`+`Expression`+`}}` (если валидна) выигрывает; `OpenBrace1` падает
  (`!"{"`). Если дыра невалидна → ошибка (CS1054/9005).
- K=3 → `HoleB` = `{{{`+`Expression`+`}}` выигрывает (1 литер. `{` + дыра `{{…}}`);
  `HoleA` падает (после `{{` идёт `{`, не `Expression`).
- K≥4 → ни `HoleA`, ни `HoleB`, ни `OpenBrace1` не матчат → **ошибка** (CS9006).

Закрывающие: M=1 → `CloseBrace1` (литер.); M≥2 → нет альтернативы → **ошибка** (CS9007).

### 2.7. D=3 — raw `$$$"""..."""`

Паттерн тот же; литеральные запуски 1..D−1 и дыры D..2D−1 (с префиксом 0..D−1):

```
RawInterpolatedStringLiteral3 = "$$$" "\"\"\"" RawPart3* "\"\"\"";

RawPart3 =
    | HoleC = "{{{{{" Expression RawFormat? "}}}"   // K=5: 2 литер. + дыра
    | HoleB = "{{{{"  Expression RawFormat? "}}}"   // K=4: 1 литер. + дыра
    | HoleA = "{{{"   Expression RawFormat? "}}}"   // K=3: дыра
    | OpenBrace2 = "{{" !"{"                          // K=2: литер.
    | OpenBrace1 = "{"  !"{"                          // K=1: литер.
    | CloseBrace2 = "}}" !'}'                         // M=2: литер.
    | CloseBrace1 = "}"  !'}'                         // M=1: литер.
    | Quote1 = "\"" !'"'
    | Quote2 = "\"" "\"" !'"'
    | Text = InterpolatedRawText;
```

- `{`-ран: K=1,2 → литер.; K=3,4,5 → дыра (с префиксом 0,1,2); K≥6 → ошибка (CS9006).
- `}`-ран: M=1,2 → литер.; M≥3 → ошибка (CS9007).

> Обобщение (для справки, **не** часть текущего дизайна): для уровня D литеральные
> `{`-раны = 1..D−1 (каждый с `!"{"`), дыры = запуски D..2D−1 (префикс 0..D−1), литеральные
> `}`-раны = 1..D−1 (каждый с `!'}'`). Именно это обобщение сворачивает Этап 5 в одно
> параметризуемое правило.

### 2.8. Подключение к `Expression` (через слияние)

Интерполированные строки — **выражения-литералы**, не константы. В `Cs6.grammar`
(см. §4) добавляются альтернативы в `Expression`:

```
Expression =
    | InterpolatedStringLiteral
    | VerbatimInterpolatedStringLiteral
    | RawInterpolatedStringLiteral
    | RawInterpolatedStringLiteral2
    | RawInterpolatedStringLiteral3;
```

(Повторное имя `Expression` в более новом файле = **добавление** альтернатив, T0.3. Эти
альтернативы не саморекурсивны → становятся **prefix** альтернативами TDOPP-правила
`Expression` — ровно то, что нужно для литералов.)

---

## 3. Временное правило `Expression` (Cs1.grammar)

**Временное** — выражения C# 1.0, минимально достаточное для контента дыр. **T2.3 заменит**
на полную TDOPP-таблицу приоритетов. Пометка в файле обязательна.

Механика TDOPP в движке (`Parser.cs:111-150`, `BuildTdoppRulesInternal`): альтернатива,
начинающаяся с `Ref(self)` и содержащая `ReqRef`, → **postfix** (бинарный оператор с
приоритетом); иначе → **prefix**. `precedence a, b, c;` → `a`=макс. binding power,
`c`=мин. (первое имя связывает сильнее). `CSharpParser` уже вызывает `BuildTdoppRules()`.

```
// === ВРЕМЕННОЕ (T3.5.1) — T2.3 заменит на полную TDOPP-таблицу C# 1.0 ===
precedence
    Unary, Multiplicative, Additive, Relational, Equality,
    LogicalAnd, LogicalXor, LogicalOr, CondAnd, CondOr,
    Conditional, Assignment, Comma;

Expression =
    | PrimaryExpr   = Primary PostfixOp*
    | UnaryPlus     = "+"  Expression : Unary
    | UnaryMinus    = "-"  Expression : Unary
    | UnaryNot      = "!"  Expression : Unary
    | UnaryBitNot   = "~"  Expression : Unary
    | PreInc        = "++" Expression : Unary
    | PreDec        = "--" Expression : Unary
    | Mul           = Expression "*"  Expression : Multiplicative
    | Div           = Expression "/"  Expression : Multiplicative
    | Mod           = Expression "%"  Expression : Multiplicative
    | Add           = Expression "+"  Expression : Additive
    | Sub           = Expression "-"  Expression : Additive
    | Less          = Expression "<"  Expression : Relational
    | Greater       = Expression ">"  Expression : Relational
    | LessEq        = Expression "<=" Expression : Relational
    | GreaterEq     = Expression ">=" Expression : Relational
    | Equal         = Expression "==" Expression : Equality
    | NotEqual      = Expression "!=" Expression : Equality
    | BitAnd        = Expression "&"  Expression : LogicalAnd
    | BitXor        = Expression "^"  Expression : LogicalXor
    | BitOr         = Expression "|"  Expression : LogicalOr
    | And           = Expression "&&" Expression : CondAnd
    | Or            = Expression "||" Expression : CondOr
    | Conditional   = Expression "?" Expression ":" Expression : Conditional
    | Assign        = Expression "="  Expression : Assignment, right
    | Comma         = Expression ","  Expression : Comma;

Primary =
    | "true"
    | "false"
    | "null"
    | "this"
    | Identifier
    | StringLiteral
    | VerbatimStringLiteral
    | CharLiteral
    | DecInt  = DecimalIntegerLiteral IntegerSuffix?
    | HexInt  = HexIntegerLiteral  IntegerSuffix?
    | Real    = DecimalRealLiteral Exponent? RealSuffix?
    | Parens  = "(" Expression ")";

PostfixOp =
    | MemberAccess = "." Identifier
    | Indexer      = "[" (Expression ("," Expression)*)? "]"
    | Invocation   = "(" (Expression ("," Expression)*)? ")"
    | PostInc      = "++"
    | PostDec      = "--";
```

Покрываемый контент дыр: `{x}`, `{obj.Prop}`, `{arr[i]}`, `{M(a, b)}`, `{x + 1}`,
`{a == b}`, `{flag ? "y" : "n"}`, `{s = "a"}`, `{ $"a{b}" }` (вложенная интерполяция),
`{ (a + (b * c)) }`, `{ s = "a } b" }` (`}` в строке не закрывает дыру — вложенный
литерал через `Primary → StringLiteral`).

**Сознательно опущено** (придёт с T2.3 / T1.4): `new`/array-initializer (`{…}` в дыре
через `new T[]{…}` — нужны типы, T1.4), приведения/`is`/`as`/`typeof`, shift `<<`/`>>`,
`checked`/`unchecked`, лямбды (CS3). См. §6 (тест «скобки в дыре» без них) и §7.

### 3.1. Формат vs условное `?:` (расхождение с Roslyn — задокументировать)

В реальном C# формат запускает **первое верхнеуровневое `:`** (лексер, §1.4), поэтому
верхнеуровневый `?:` **в дыре не разрешён**: `$"{a ? b : c}"` в Roslyn = expr `a ? b` +
format ` c` → `a ? b` — неполное условие → **ошибка**. Грамматический подход с полным
TDOPP `Expression` разберёт `{a ? b : c}` как условие (без формата) — **расхождение**
(грамматика более permisсивна). Обычный случай `{x : F2}` матчится корректно
(`Expression` не потребляет `:`, т.к. `:` — не оператор). Решение — в T2.3 (ограниченный
`HoleExpression`, не потребляющий верхнеуровневый `:`, или явный format-first).
До T2.3 — задокументированное ограничение (§7).

> В обычном случае `{x : F2}` `Expression` **не потребляет** `:` (он не оператор), поэтому
> формат матчится корректно; расхождение возникает только при верхнеуровневом `?:`.

---

## 4. Организация файлов

**Решение: правила интерполяции → новый `Cs6.grammar`; временное `Expression` → `Cs1.grammar`;
слияние Cs1+Cs6 через существующий API `CSharpParser(IReadOnlyList<(Text,Path)>, …)`.**

Обоснование:
1. **Version-purity (T1.3.1)**: интерполяция — фича **C# 6** (T3.5). Правила должны жить в
   `Cs6.grammar`, а не в `Cs1.grammar` (иначе Cs1 «знает» CS6-фичу). Временное `Expression`
   — фича **C# 1.0** (выражения существовали с C# 1.0) → `Cs1.grammar`.
2. **Итерация CS6 (T3.5.3)** добавит в тот же `Cs6.grammar` остальные фичи (`?.`,
   expression-bodied члены, `nameof`, binary literals) — единый файл на версию, как в плане.
3. **Слияние (T0.3)**: повторное имя правила в более новом файле = добавление альтернатив.
   `Cs6.grammar` переобъявляет `Expression` (добавляет 5 строковых альтернатив) + вводит
   новые правила (`InterpolatedStringLiteral` и др.). `BuildTdoppRules` после слияния
   корректно раскладывает объединённый `Expression` (строки = prefix).
4. **`Constant` не трогается**: интерполяция — не константа; альтернативы идут в
   `Expression`, а не в `Constant` (`Cs1.grammar:120-128` ссылается только на
   `StringLiteral`/`VerbatimStringLiteral` — не-интерполированные терминалы, остаются).

Структура после T3.5.2:
- `Cs1.grammar` — текущая + `precedence …;` + временное `Expression`/`Primary`/`PostfixOp`.
- `Cs6.grammar` (новый) — 5 правил интерполяции + `precedence`-не требуется (строки — не
  операторы) + переобъявление `Expression` (5 альтернатив).
- Сборка: `new CSharpParser([(LoadCs1(), "Cs1.grammar"), (LoadCs6(), "Cs6.grammar")], trivia, terminals)`.
  `Cs6.grammar` добавить в `<EmbeddedResource>` (`CSharpGrammar.csproj:18-20`).

> Альтернатива (отклонена): всё в `Cs1.grammar` — ломает version-purity и смешивает CS1/CS6.
> Альтернатива (отклонена): `Expression` в `Cs6.grammar` — вынуждает Cs1-фрагменты не иметь
> выражений и усложняет T2.3 (выражения — CS1.0, должны быть в Cs1).

---

## 5. Удаление (T3.5.2)

**Удаляются** (сканерная интерполяция):
- `CSharpTerminals.cs`: фабрики/поля/records `InterpolatedStringLiteral()` и
  `RawInterpolatedStringLiteral()`; их удаление из `GetAll()` (`:59`, `:62`).
- `StringLiteralScanner.cs`: `TryScanInterpolatedString`, `TryScanAtInterpolatedString`,
  `TryScanRawInterpolatedString`, `TryScanRawContents` (raw-ветка), raw-ветки в
  `TryScanStringContents` (`case '{'`/`'}'` raw, `case '"'` raw-ран), `TryScanHoleBalancedText`,
  `TryScanBracketed`, `TryScanFormatSpecifier`, `TryScanNestedLiteral`,
  `TryScanPlainOrRawString`, `TryScanCharLiteral*` (hole-поддержка), helpers
  `CountRun`/`ConsumeWhitespace`/`SkipNewLine`/`IsIllegalEmptyMultiLineRaw`/`IsRawWhitespace`
  (если не используются не-интерполированными путями).

**Остаются** (не-интерполированные — hand-written терминалы, DFA/сканер корректны):
- `StringLiteral` (`TryScanPlainString`), `VerbatimStringLiteral` (`TryScanVerbatimString`),
  `RawStringLiteral` (`TryScanRawString`) + их сканерные пути (`TryScanStringContents`
  Normal/Verbatim **без** `$`-веток, plain-raw). `CharLiteral` (`TryScanCharLiteral`) —
  остаётся как терминал, но hole-вызовы из него уходят.
- `CSharpTerminals.cs`: `StringLiteral()`, `VerbatimStringLiteral()`, `RawStringLiteral()`,
  `CharLiteral()` и остальные не-строковые терминалы.

**Заменяются**: терминалы `InterpolatedStringLiteral`/`RawInterpolatedStringLiteral` →
грамматические правила (§2) + новые терминалы-раны/escape/формат (§2.2).

---

## 6. План тестов (T3.5.2)

Фрагментный парсинг: `parser.Parse(input, startRule, out triviaLength)`; успех =
`result.TryGetSuccess(out _, out end) && end == input.Length && parser.Parser.ErrorInfo is null`.
Парсер строится из слияния `[(Cs1, "Cs1.grammar"), (Cs6, "Cs6.grammar")]`.
`startRule` — на каждое семейство/уровень своё правило (§2.8).

### 6.1. Валидные (по семейству × уровню $)

| startRule | Фрагменты |
|---|---|
| `InterpolatedStringLiteral` | `$""`, `$"abc"`, `$"{x}"`, `$"{{x}}"` (escape), `$"a {x} b"`, `$"{x:0}"` (format), `$"{s = "a"}"` (вложенный литерал), `$"{ $"a{b}c" }"` (вложенная интерполяция), `$"{ (a + (b * c)) }"` (вложенные скобки), `$"{ s = "a } b" }"` (`}` в строке), `$"{(a ? "y" : "n")}"` (условие **в скобках** — валидно и в Roslyn: `:` вложен) |
| `VerbatimInterpolatedStringLiteral` | `$@""`, `@$""`, `$@"{x}"`, `$@"{{x}}"`, `$@""""` (escape `""`), `$@"{x:0}"`, `$@"a\nb"` (newline + `\`-контент), `$@"{ @""a}b"" }"` (вложенная verbatim) |
| `RawInterpolatedStringLiteral` (D=1) | `$"""{x}"""`, `$"""abc"""`, `$"""{x:0}"""`, `$"""{ $"a{b}" }"""` (вложенная), single-line и multi-line (`$"""\n{ x }\n"""`) |
| `RawInterpolatedStringLiteral2` (D=2) | `$$"""{{x}}"""` (дыра `{{ }}`), `$$"""{{{x}}}"` (K=3: 1 литер. + дыра), `$$"""{x}"""` (K=1 = литер. текст), `$$"""}}"""`→см. невалидные, `$$"""{{x:0}}"""` (format) |
| `RawInterpolatedStringLiteral3` (D=3) | `$$$"""{{{x}}}"""` (дыра), `$$$"""{{x}}"""` (K=2 = литер.), `$$$"""{{{{x}}}}"""`→см. невалидные, `$$$"""{{{x:0}}}"""` (format) |

Специальные: **вложенные интерполяции в дырах** (все семейства), **format-спецификаторы**
(все), **экранированные скобки/кавычки** (regular `{{`/`}}`/`\`, verbatim `{{`/`}}`/`""`),
**литеральные запуски скобок** (raw D=2: `{`; D=3: `{`, `{{`), **multi-line raw** (пустая
строка контента, ws перед close).

> **Расхождение (зафиксировать отдельным тестом-документацией)**: `$"{a ? "y" : "n"}"`
> (условие **без** скобок) грамматика принимает как условие (§3.1), Roslyn отклоняет
> (`:` = формат). Это НЕ валидный C# — тест фиксирует текущее поведение временного
> `Expression`; после T2.3 (ограниченный `HoleExpression`) ожидается отклонение.

> «Лямбды с `{ }` в дырах» — **отложено** (лямбды = CS3, не во временном `Expression`);
> свойство «баланс скобок в дыре» покрывается вложенными интерполяциями/скобками/строками
> выше. Полноценный тест лямбдой — после T2.3/T3.2.

### 6.2. Невалидные

| startRule | Фрагменты (ожидаемо: `end != length` / `ErrorInfo != null`) |
|---|---|
| `InterpolatedStringLiteral` | `"$\"}"` (stray `}`, CS1052), `"$\"{a"` (незакрытая дыра, CS1054), `"$\"abc"` (unterminated), `"$\"\\u007B\""` (`\{`, CS1053), `"$\"{x:{}}\""` (`{` в формате) |
| `VerbatimInterpolatedStringLiteral` | `$@"{x"` (незакрытая дыра), `$@"}"` (stray `}`), `$@"a` (unterminated) |
| `RawInterpolatedStringLiteral` (D=1) | `$"""{x}"""` с stray `}` → `$"""{x}""""`… ; `$"""{{x}}"""` (`{{` = CS9006), `$"""{x""""` (unterminated) |
| `RawInterpolatedStringLiteral2` (D=2) | `$$"""{{{{x}}}}"""` (K=4 ≥ 2D, CS9006), `$$"""{{x}"""` (close-ран 1 < D, CS9005), `$$"""{x}}"""` (`}}`-ран в контенте = D, CS9007) |
| `RawInterpolatedStringLiteral3` (D=3) | `$$$"""{{{{{{x}}}}}}"""` (K=6 ≥ 2D, CS9006), `$$$"""{{{x}}"""` (close-ран 2 < D, CS9005) |

Каждый невалидный кейс сверить с кодом ошибки Roslyn (§1.6) и табл. T1.2.5.

### 6.3. Newline-прозрачность — тесты-документации (grammar принимает, Roslyn отклоняет)

Причина — глобальный trivia-сканер (§2.2, §7.5): newline/whitespace между терминалами внутри
строки съедаются trivia, поэтому grammar **не может** отклонить newline там, где Roslyn
отклоняет. Тесты фиксируют, что grammar **принимает** (`TryGetSuccess && end == length &&
ErrorInfo is null`) ввод, который Roslyn отвергает; после каждого — код ошибки Roslyn.
Локально неисправимо (trivia не знает, что он внутри строки).

| startRule | Фрагмент (grammar: **принимает**) | Roslyn |
|---|---|---|
| `InterpolatedStringLiteral` | `"$\"a\nb\""` (newline в regular-тексте) | CS1039 `ERR_UnterminatedStringLit` |
| `RawInterpolatedStringLiteral` | `$"""a\nb"""` (newline в single-line raw) | CS8997 `ERR_UnterminatedRawString` |
| `RawInterpolatedStringLiteral` | `$"""\n"""` (пустое тело multi-line) | CS9002 `ERR_RawStringMustContainContent` |
| `RawInterpolatedStringLiteral` | `$"""\nabc\ndef """` (delimiter в середине последней строки, EOF после) | CS9000 `ERR_RawStringDelimiterOnOwnLine` |

> Валидный код не затронут: корректные single/multi-line raw с дырами и regular/verbatim
> без newline парсятся идентично Roslyn. Расхождения — только невалидный ввод, который
> grammar дополнительно принимает.

---

## 7. Ограничения (задокументировать, **не** решать)

### 7.1. Raw с 4+ кавычками (N≥4)

Отложено (план, раздел «Риски»: hand-written `Terminal` / контекстный парсер). Дизайн —
только N=3. N>3 сейчас не матчится. (См. также §7.5: без guard `!'"'` 6 кавычек `$""""""`
принимаются как пустое тело — в рамках этого же отложенного N>3.)

### 7.2. Общее N-$

**Этап 5** (параметризуемые правила `Content(int localCount)`, `Count(Dollars)`, вычисленное
повторение `'{'{localCount}`). 3 явных уровня — временная форма; §2.7 даёт обобщение, которое
Этап 5 сворачивает в одно правило.

### 7.3. Формат vs условное `?:`

Расхождение с Roslyn (§3.1): грамматика может принять `{a ? b : c}` как условие, Roslyn
ошибается. Решить в T2.3.

### 7.4. D1 (epsilon на start rule)

Бэклог движка: пустой ввод / только trivia не парсится на start rule. Фрагментные тесты
(§6) используют **непустые** start rules (`InterpolatedStringLiteral` и др.), не `Grammar`
— обходят D1. Влияние на полный `Grammar`-парсинг — territory D1/T4.1.

### 7.5. Newline-прозрачность (грамматика более permisсивна, чем Roslyn)

Причина — глобальный trivia-сканер (§2.2): newline/whitespace между терминалами внутри
строки съедаются trivia, поэтому grammar **не может** отклонить newline там, где Roslyn
отклоняет. Grammar **принимает невалидный код**; локальными альтернативами **неисправимо**
(trivia не знает, что он внутри строки). Четыре расхождения (тесты-документации — §6.3):

| # | Случай | Grammar | Roslyn (код, где) |
|---|---|---|---|
| 1 | newline в тексте **regular** | принимает | CS1039 `ERR_UnterminatedStringLit`, `Lexer_StringLiteral.cs:556-558` |
| 2 | newline в **single-line raw** | принимает | CS8997 `ERR_UnterminatedRawString`, `Lexer_StringLiteral.cs:581` |
| 3 | **пустое тело** multi-line raw (`$"""\n"""`) | принимает | CS9002 `ERR_RawStringMustContainContent`, `Lexer_StringLiteral.cs:732` |
| 4 | закрывающий delimiter **в середине строки** (multi-line raw) | принимает | CS9000 `ERR_RawStringDelimiterOnOwnLine`, `Lexer_StringLiteral.cs:628` |

**Валидный код не затронут**: корректные single-line и multi-line raw с дырами парсятся
идентично Roslyn; расхождения касаются только невалидного ввода, который grammar
дополнительно принимает.

> Связанно (не newline, а «лишний ввод»): избыток закрывающих кавычек (CS8998
> `ERR_TooManyQuotesForRawString`, `:594-601`) выливается в «лишний» ввод после строки →
> фрагмент `end != input.Length` → отклоняется (§1.6). Точное совпадение с
> Roslyn-диагностиками — за рамками T3.5.2.

### 7.6. Временное `Expression`

Минимальный набор (§3); `new`/array-init, приведения, `is`/`as`, shift, лямбды — после
T2.3/T1.4/T3.2. До этого «скобки в дыре» покрываются вложенными интерполяциями/скобками/
строками, а не `new T[]{…}`/лямбдами.

---

## Приложения

### A. Соответствие «уровень $ → правило → startRule»

| D | Семейство | Правило | startRule (тест) |
|---|---|---|---|
| 1 | regular | `InterpolatedStringLiteral` | `InterpolatedStringLiteral` |
| 1 | verbatim | `VerbatimInterpolatedStringLiteral` | `VerbatimInterpolatedStringLiteral` |
| 1 | raw | `RawInterpolatedStringLiteral` | `RawInterpolatedStringLiteral` |
| 2 | raw | `RawInterpolatedStringLiteral2` | `RawInterpolatedStringLiteral2` |
| 3 | raw | `RawInterpolatedStringLiteral3` | `RawInterpolatedStringLiteral3` |

### B. Чек-лист выразимости в текущем языке

- [x] Только `Seq`/`Alt`/`Ref`/`Literal`/`OneOrMany`/`ZeroOrMany`/`Optional`/`!`/TDOPP.
- [x] Нет параметризуемых правил, `Count()`, вычисленных повторений.
- [x] «Запуск ≥ D = дыра, иначе текст» — через литералы заданной длины + `!`-lookahead.
- [x] Longest-match без равных длин (проверено по позициям `{`/`}`/`"` для D=1,2,3).
- [x] TDOPP `Expression` — `precedence` + `ReqRef`, `BuildTdoppRules()` уже вызывается.
- [x] Слияние Cs1+Cs6 — существующий API (T0.3), повторное имя = добавление альтернатив.
