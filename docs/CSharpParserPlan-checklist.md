# CSharpParserPlan — execution checklist

Plan: `docs/CSharpParserPlan.md`. One subagent per sub-point. Progress files: `docs/CSharpParserPlan-progress<subpoint>.md`.

## Этап 0 — Инфраструктура

- [✅] T0.1 Проект `Parsers/CSharp/CSharpGrammar`, добавить в `Nitra.sln`, оболочка `CSharpParser` (сборка `Parser` из текста грамматики)
- [✅] T0.2 Тестовый проект `Tests/CSharpGrammarTests` (MSTest, net8.0) + зелёный smoke-тест
- [✅] T0.3 Слияние версий: повторное имя правила в более новом файле = добавление альтернатив
  - [✅] T0.3.1 Реализация: слияние нескольких текстов грамматики (TypeChecker/RuleGenerator + API `CSharpParser`)
  - [✅] T0.3.2 Метациркулярные тесты слияния

## Этап 1 — C# 1.0: compilation unit → типы

- [✅] T1.1 (Исследование, субагент + roslyn MCP) Карта Roslyn → `docs/RoslynGrammarMap.md`
- [✅] T1.2 Терминалы C# (идентификатор, числа, char/строки, пунктуация, trivia/комментарии)
  - [✅] T1.2.1 Реализация `CSharpTerminals` в CSharpGrammar (строки — переделаны в T1.2.3–T1.2.5)
  - [✅] T1.2.2 Тесты терминалов (тесты строк переписаны в T1.2.3–T1.2.5)
  - [✅] T1.2.3 Ревёрк: обычные строки `"..."` + интерполированные `$"..."` — общий $-совместимый рекурсивный матчер + тесты
  - [✅] T1.2.4 Ревёрк: verbatim `@"..."` + `$@"..."/@$"..."` (на общем ядре) + тесты
  - [✅] T1.2.5 Ревёрк: raw-строки (N≥3 кавычки) + raw-интерполированные (D≥1 `$`, дыры) + тесты (интерполированная часть заменится грамматикой в T3.5)
- [✅] T1.3 Грамматика: compilation unit, `using`, `extern alias`, `namespace` (block), namespace-члены (class/struct/enum/interface/delegate — заголовки)
  - [✅] T1.3.1 Ревёрк: version-purity + обязательные символы + нативные литералы вместо Kw*
    - [✅] T1.3.1.1 Фреймворк: `WordLiteral` + RuleGenerator (identifier-подобные литералы текста грамматики = whole-word)
    - [✅] T1.3.1.2 `Cs1.grammar`: Kw* → нативные литералы; удалить 82 Kw-терминала; завершить верификацию дефектов (extern alias, модификаторы, trailing-запятые, Cs1-keywords)
  - [✅] T1.3.2 Тесты: прогон всего репозитория Roslyn, отбор тестов для Cs1 (138: 71 pos / 67 neg)
- [✅] T1.4 Грамматика: типы C# 1.0 (преопределённые, квалифицированные, массивы, указатели) + тесты (фича + тесты + отбор из Roslyn одним субагентом)

## Дефекты/бэклог движка (ExtensibleParser)

- [ ] D1: пустой ввод ("" / только комментарии/trivia) не парсится — epsilon-совпадение именованного правила на стартовом правиле отклоняется (`AcceptEpsilonMatch`: NoRecovery=false, Recovery=только RecoveryRule в recovery-точке). Валидный C# (пустой файл) падает. Нужено решение уровня движка (принять epsilon для start rule / явный путь EOF) с защитой от бесконечных циклов. Обойдено в грамматике для NamespaceBody (`NamespaceBody?`) — для start rule обхода нет.
- [✅] D2: `int.Parse()` / `string.Format()` не парсились — guard `IdentifierName = !ReservedKeyword Identifier` в `Primary` (T2.3.1) блокировал ВСЕ reserved-ключевые слова как начало Expression, включая predefined-типы. Исправлено в T2.3.3: добавлена альтернатива `PredefinedMember = PredefinedType "." !ReservedKeyword Identifier` (predefined-тип допустим как начало Expression только с обязательным `.` + identifier; bare `int` остаётся не-выражением; guard для `new`/`typeof`/`sizeof` не тронут).

## Этап 2 — C# 1.0: члены и тела

Выполняется в порядке зависимостей (см. Deviations): **T2.3 (выражения) → T2.2 (операторы) → T2.1 (члены)**. Фундамент — временное TDOPP-правило `Expression` из T3.5.2 (уже есть полная таблица приоритетов, `Primary`, `PostfixOp`).

- [✅] T2.3 Выражения C# 1: завершить временное `Expression` (TDOPP) — `new`, приведение, `is`/`as`, `sizeof`/`typeof` + тесты
  - [✅] T2.3.1 `new` (object/array creation) + `is`/`as` (type test в TDOPP-таблице) + `sizeof`/`typeof` + тесты (фича + Roslyn-отбор)
  - [✅] T2.3.2 Приведение `(Type) expr` (разборность cast vs parens — см. Deviations) + тесты
  - [✅] T2.3.3 Дефект D2: predefined-типы как начало Expression (`int.Parse()`, `string.Format()`) — уточнить guard в `Primary` (разрешить type-name starts, не все reserved words) + тесты
- [ ] T2.2 Операторы: `Block` + if/while/do-while/for/foreach/switch/try/using/lock/checked/return/goto/throw/block/empty/declaration + тесты
  - [✅] T2.2.1 `Block` + empty + expression-stmt + local-variable-declaration + return/throw/break/continue/goto/labels + тесты
  - [✅] T2.2.2 if/while/do-while/for/foreach + тесты
  - [ ] T2.2.3 switch (case/default/labels) + try-catch-finally + using/lock/checked/unchecked + тесты
- [ ] T2.1 Члены: поля, свойства, методы, конструкторы, деструкторы, операторы, индексаторы, события, модификаторы, атрибуты + тесты
  - [ ] T2.1.1 Field + member-модификаторы + field-initializer + тесты
  - [ ] T2.1.2 Property + accessors (get/set) + property-модификаторы + тесты
  - [ ] T2.1.3 Method + constructor (+ `: base`/`: this`) + destructor + method-модификаторы + тесты
  - [ ] T2.1.4 Operator + indexer + event + тесты
  - [ ] T2.1.5 `ClassBody`/`StructBody`/`InterfaceBody` → member lists; base-list; version-purity + тесты

## Этап 3 — Версии CS2–CS14

- [ ] T3.1 CS2: дженерики + constraints, `var`, анонимные методы, `partial`, sealed override
- [ ] T3.2 CS3: лямбды, auto-свойства, object/collection initializers, extension methods, анонимные типы, LINQ-запросы
- [ ] T3.3 CS4: `dynamic`, именованные/опциональные аргументы, `params`-массив, constraint `new`
- [ ] T3.4 CS5: `async`/`await`
- [ ] T3.5 CS6: `?.`, expression-bodied члены, `nameof`, binary literals (интерполяция — T3.5.1–T3.5.2, выполняется раньше остальных пунктов плана)
  - [✅] T3.5.1 Дизайн: правила интерполяции (3 уровня $) для семейств regular/verbatim/raw → `docs/InterpolatedStringGrammar.md` (семантика из Roslyn Lexer + образец `C:\RSDN\nitra\...\CS6Literals.nitra`)
  - [✅] T3.5.2 Реализация: грамматические правила интерполяции + временное правило Expression + удаление сканер-терминалов (`InterpolatedStringLiteral`, `RawInterpolatedStringLiteral`, hole-скан) и их тестов
  - [ ] T3.5.3 CS6: `?.`, expression-bodied члены, `nameof`, binary literals (итерация CS6)
- [ ] T3.6 CS7: кортежи, pattern matching, локальные функции, `out var`, `ref`-возврат/локальные, разделители цифр, `throw`-выражение, `ref readonly`
- [ ] T3.7 CS7.1–7.2: `default`, `in`, `ref struct`, type/constant patterns
- [ ] T3.8 CS8: switch expressions, using declarations, `..`/`^`, `??=`, NRT-аннотации, default interface members
- [ ] T3.9 CS9: records, `with {}`, init-only, top-level statements, static abstract в интерфейсах
- [ ] T3.10 CS10: file-scoped `namespace`, `global using`
- [ ] T3.11 CS11: raw-строки, generic attributes, `required`, `params Span<T>`
- [ ] T3.12 CS12: primary constructors (классы), collection expressions, list patterns, `not`/`and`/`or`
- [ ] T3.13 CS13: extension members, `field`, `event field`
- [ ] T3.14 CS14: `ref`-поля + остаток фич по данным T1.1

## Этап 4 — Закаливание

- [ ] T4.1 Тесты разбитого кода (recovery-тесты Roslyn, согласование с `ExtensibleParser/Recovery`)
- [ ] T4.2 Производительность: бенчмарк, анализ горячих точек
- [ ] T4.3 Перенос сборки грамматики в Roslyn source generator

## Этап 5 — Парсер: передача контекста

- [✅] T5.1 Дообработка парсера/мета-грамматики: параметризуемые правила (`Content(int localCount)`), вычисление по узлу (`Count(Dollars)`), повторение с вычисленным счётчиком (`'{'{localCount}`) — общее правило raw-интерполяции вместо 3 ручных вариантов (см. план, Этап 5)

## Этап 6 — Декларативные строковые литералы

Выполнен как отдельный сабплан `DeclarativeStringTerminals` (checklist: `DeclarativeStringTerminals-checklist.md` в корне; progress1–3). Подход отклонён от исходного дизайна Этапа 6: вместо расширения языка CsNitra char-классами (T6.1) и no-trivia маркера движка (T6.2) — **[Regex]-терминалы** (regex нативно поддерживает классы символов) + правила грамматик. См. Deviations.

- [✅] T6.1 char-классы «раны символов» — заменены [Regex]-терминалами (regex-классы `[...]`/`[^...]`); язык CsNitra не расширялся
- [✅] T6.2 no-trivia маркер движка — не понадобился: целые [Regex]-раны + правила обходят post-terminal trivia-skip (см. Deviations)
- [✅] T6.3 Plain (`"..."`) + verbatim (`@"..."`) декларативно (правила `Cs1.grammar`) + удаление терминалов + тесты (`StringLiteralRuleTests.cs`)
- [✅] T6.4 Raw-литералы (`"""..."""`) декларативно (`Cs11.grammar`) + удаление терминала + тесты (`RawStringLiteralRuleTests.cs`)
- [✅] T6.5 Части интерполяции (text/escape/format, `Cs6.grammar`) декларативно + удаление 7 терминалов `Interpolated*`/`*FormatText`
- [✅] T6.6 Удаление `StringLiteralScanner` + полная верификация (build + 616/616 тестов, ревизия diff)

## Deviations

- T0.1: убран redundant `<Compile Include="MiniC\MiniCGrammar.cs" />` из ParserTests.csproj (NETSDK1022, дублирование SDK-глоба) — чинил билд всего решения.
- T0.3.2: пипованные альтернативы в CsNitra-грамматике обязаны быть именованными (`| Name = Expr` или RuleRef) — `Rule = | "a";` не парсится. Учитывать во всех `CsN.grammar`.
- T0.3.2: при верификации один раз упал 1 тест ParserTests (имя не зафиксировано); 10+ последующих прогонов (полный набор + новые тесты по отдельности) — зелёные. Вероятно, изолированный флейк; наблюдать.
- T1.2.1: сбой оборудования убил субагента после написания файла, до верификации; ретрай по progress-файлу починил `and`→`&&` в boolean-контекстах + `record` для `TriviaTerminal`.
- T1.2.1: в реальном C# НЕТ восьеричных литералов (Roslyn Lexer.cs — только hex/binary); `OctalIntegerLiteral` создан, но по умолчанию не используется грамматикой (риск равной длины с decimal).
- T1.3.1: баг движка: `CsNitraVisitor` не обрабатывал SeqNode `SeparatorModifier` — ЛЮБЫЙ текст грамматики с модификатором разделителя (`: ?`/`: !`) падал с `Unknown SeqNode kind: SeparatorModifier`. Починено в `CsNitraVisitor` (кейс возвращает Literal модификатора). C# 1.0 требовал `: ?` (trailing-запятые в enum/параметрах/аргументах атрибутов).
- T1.3.1: ключевые слова — whole-word терминалы (`KeywordTerminal` в `CSharpTerminals`, 82 шт.): литералы-ключевые слова матчат по префиксу, а `ParseTerminal` пропускает trivia после совпадения — наивный `"kw" !IdentifierPart` сломан (видит первый символ СЛЕДУЮЩЕГО токена). Граница слова проверяется внутри `TryMatch` (`char.IsLetterOrDigit || '_'`, как `\w` в DFA). `partial` НЕ в `ReservedKeyword` (contextual + merge-friendly).
- T1.3.1: recovery: `class { }` (нет имени) НЕ восстанавливается (ε-совпадение в recovery-точке принимается только для RecoveryRule) — smoke-тест использует `class C {` (нет `}`, восстанавливается S1-вставкой). Глубокое восстановление «нет идентификатора» — territory движка/T4.1.
- T1.3.1: `static` НЕ class-modifier в Cs1 (Roslyn гейтит static classes как C# 2: MessageID.cs `IDS_FeatureStaticClasses` → CSharp2); в Cs2 добавляется `| Static = KwStatic`.
- **Решение пользователя**: T1.2 считается невыполненным — строчные литералы были «халтурными» regex (без `$`, raw, дыр; слишком lax). Ревёрк T1.2.3–T1.2.5.
- **Решение пользователя**: исследования — ТОЛЬКО код Roslyn (`C:\RSDN\roslyn`), никаких веб-поисков; портировать семантику из кода лексера Roslyn.
- **Решение пользователя**: гранулированные подзадачи — по одной на тип строкового литерала; фича + тесты в ОДНОМ субагенте (разделение impl/tests отменяется для ревёрка строк); один общий $-совместимый ядро для трёх семейств (обычные/verbatim/raw), иначе код пишется дважды.
- **Решение пользователя**: DFA/regex эту грамматику не возьмёт — парсинг строковых литералов рекурсивный: ручной матчер (кастомный `Terminal`) или грамматические правила основным парсером — решает субагент, решение задокументировать.
- T1.3.1: ревью оркестратора нашло дефекты — `KwOut` в `ParameterModifier` (out = CS2, не CS1), `AttributeList` с необязательным `"]"`, `DelegateDeclaration` с необязательным `";"`, `KwNew` в `StructModifier` (new — не типовой модификатор), нет `unsafe`. Ревёрк перед коммитом.
- T1.3.1: часть претензий ревью опровергнута субагентом по Roslyn: `out`-параметры — C# 1.0 (нет feature-gate; оставлены), `default`/`in` — reserved-ключевые слова во всех версиях (остались в ReservedKeyword), `extern alias` — парсер принимает во всех версиях (gate C#2 в байндере; восстановлен в грамматике).
- **Решение пользователя**: 82 `Kw*`-терминала — отклонены как вредительство; ключевые слова в тексте грамматики — нативные строковые литералы; whole-word семантика решена на уровне фреймворка (`WordLiteral`, T1.3.1.1).
- **Решение пользователя**: программная (сканерная) реализация ИНТЕРПОЛИРОВАННЫХ строк неприемлема (в дырах — полные выражения с вложенными интерполяциями/лямбдами); $-строки = грамматические правила с дырой `{ Expression }`; пока 3 явных варианта ($/$$/$$$), общее — после Этапа 5 (передача контекста в парсере).
- T1.3.2: движок отклоняет epsilon-совпадение именованных правил — пустое тело namespace обходится `NamespaceBody?`; пустой ввод (start rule) — в бэклоге движка (D1).
- **Решение пользователя**: строковые литералы переписываются ДО остальных пунктов плана: сначала T3.5.1–T3.5.2 (интерполяция = грамматические правила, 3 уровня $), затем продолжение плана с T1.4. T3.5 расщеплён: T3.5.1 дизайн, T3.5.2 реализация строк, T3.5.3 — остальные фичи CS6 (итерация CS6).
- T3.5.1: верификация оркестратора нашла дефект дизайна — движок пропускает trailing trivia после КАЖДОГО терминала (включая литералы, `Literal : Terminal`), а `TriviaTerminal` ест все whitespace/newlines → внутри грамматической строки newlines «прозрачны»: ветки MultiLine/SingleLine raw мертвы, 4 расхождения accept/reject (грамматика более permisсивна: CS1039/CS8997/CS9002/CS9000). Исправлено в дизайне (один контентный цикл, жадные text-раны, §6.3/§7.5). Учтено в T3.5.2.
- T3.5.2: первая сессия субагента умерла (пустой результат, progress-файл-каркас); ретрай по состоянию на диске. Найден дефект: D=3 raw-дыры закрывались 4 скобками — опечатка была и в дизайне (§2.7), исправлена в реализации И в дизайн-документе (дыра уровня D закрывается D скобками). Убраны 2 отладочных теста.
- T3.5.2: отклонения от дизайна (задокументированы в progress): `PostfixOp` через `SeparatedList` (не имена анонимных групп); `VerbatimInterpolatedPrefix` — отдельное правило (нет `|` внутри группы); char-литералы в `!`-предикатах → string-литералы (язык поддерживает только double-quoted).
- **Этап 6**: подход отклонён от исходного дизайна плана. T6.1 (char-классы в языке CsNitra) и T6.2 (no-trivia маркер движка) **не реализованы как описано** — вместо них [Regex]-терминалы: regex-движок нативно поддерживает классы символов (`[^\{\}\\"]+`), а целые [Regex]-раны + правила грамматик обходят проблему post-terminal trivia-skip без изменения движка. Результат цели этапа достигнут (все строковые литералы декларативны, `StringLiteralScanner` удалён). Детали + задокументированные accept/reject-расхождения (D1–D7) — в `DeclarativeStringTerminals-checklist.md`.
- **Этап 2**: порядок выполнения изменён на **T2.3 → T2.2 → T2.1** (в плане T2.1 → T2.2 → T2.3). Причина: члены (T2.1) содержат тела-блоки из операторов (T2.2), операторы содержат выражения (T2.3) — жёсткая зависимость «сверху вниз». Временное TDOPP-правило `Expression` (T3.5.2) уже содержит полную таблицу приоритетов, поэтому его завершение (T2.3) — естественный фундамент. Номера задач T2.1/T2.2/T2.3 сохранены за соответствующим содержанием (члены/операторы/выражения) для прослеживаемости.
- T2.3.1: `new Foo` (без скобок/размеров/инициализатора) — NEGATIVE (в задании было positive): Roslyn `ERR_BadNewExpr` (CS1526). `new Foo()`/`new Foo(1,2)` — positive.
- T2.3.1: механизм Type-операнда для `is`/`as` (RHS = Type, не Expression) — декларативно через self-recursive альтернативу `Expression : Relational "is" Type` (прецедент postfix берётся из первого элемента-ReqRef, RHS — plain `Ref("Type")` на prec 0). Тот же паттерн, что у postfix'ов самого `Type`. Без изменения движка.
- T2.3.1: добавлен guard `IdentifierName = !ReservedKeyword Identifier` в `Primary` (иначе `new`/`typeof`/`sizeof` глотались бы как identifier).
- T2.3.2: cast `(Type) expr` — декларативно, без изменения движка. `CastExpr = "(" Type ")" Expression : Cast` — TDOPP **prefix**-оператор на новом самом жёстком уровне `Cast` (выше `Unary`); операнд — ReqRef на `Cast`, поэтому не поглощает бинарные операторы: `(int) x + y` → `((int)x)+y`. Разграничение с `(expr)`: `PrimaryExpr` (с `Parens`) — первая prefix-альтернатива, longest-match + тай-брейк «первый выигрывает».
- T2.3.2: `(int) x.y` → **`(int)(x.y)`**, НЕ `((int)x).y` (операнд каста — unary-expression, включает member-access; Roslyn `ParseSubExpression(Cast)`→`ParsePrimaryExpression`). Поправка к допущению в задаче.
