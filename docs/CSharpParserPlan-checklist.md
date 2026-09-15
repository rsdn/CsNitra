# CSharpParserPlan — execution checklist

Plan: `docs/CSharpParserPlan.md`. One subagent per sub-point. Progress files: `docs/CSharpParserPlan-progress<subpoint>.md`.

## Этап 0 — Инфраструктура

- [✅] T0.1 Проект `Parsers/CSharp/CSharpGrammar`, добавить в `Nitra.sln`, оболочка `CSharpParser` (сборка `Parser` из текста грамматики)
- [~] T0.2 Тестовый проект `Tests/CSharpGrammarTests` (MSTest, net8.0) + зелёный smoke-тест
- [✅] T0.3 Слияние версий: повторное имя правила в более новом файле = добавление альтернатив
  - [✅] T0.3.1 Реализация: слияние нескольких текстов грамматики (TypeChecker/RuleGenerator + API `CSharpParser`)
  - [✅] T0.3.2 Метациркулярные тесты слияния

## Этап 1 — C# 1.0: compilation unit → типы

- [~] T1.1 (Исследование, субагент + roslyn MCP) Карта Roslyn → `docs/RoslynGrammarMap.md`
- [~] T1.2 Терминалы C# (идентификатор, числа, char/строки, пунктуация, trivia/комментарии)
  - [✅] T1.2.1 Реализация `CSharpTerminals` в CSharpGrammar (строки — переделаны в T1.2.3–T1.2.5)
  - [✅] T1.2.2 Тесты терминалов (тесты строк переписаны в T1.2.3–T1.2.5)
  - [~] T1.2.3 Ревёрк: обычные строки `"..."` + интерполированные `$"..."` — общий $-совместимый рекурсивный матчер + тесты
  - [ ] T1.2.4 Ревёрк: verbatim `@"..."` + `$@"..."` (на общем ядре) + тесты
  - [ ] T1.2.5 Ревёрк: raw-строки (N≥3 кавычки) + raw-интерполированные с дырами + тесты
- [~] T1.3 Грамматика: compilation unit, `using`, `extern alias`, `namespace` (block), namespace-члены (class/struct/enum/interface/delegate — заголовки)
  - [~] T1.3.1 Ревёрк: version-purity + обязательные символы (найдены в ревью: `out` в Cs1, `"]"?`, `";"?` у delegate, `KwNew` в StructModifier, нет `unsafe`)
  - [ ] T1.3.2 Тесты (примеры из Roslyn + простые кейсы)
- [ ] T1.4 Грамматика: типы C# 1.0 (преопределённые, квалифицированные, массивы, указатели) + тесты

## Этап 2 — C# 1.0: члены и тела

- [ ] T2.1 Члены: поля, свойства, методы, конструкторы, деструкторы, операторы, индексаторы, события, модификаторы, атрибуты + тесты
- [ ] T2.2 Операторы/выражения-вызовы: if/while/for/foreach/switch/try/using/lock/check/return/goto/throw/block/empty + тесты
- [ ] T2.3 Выражения C# 1: TDOPP-таблица приоритетов, литералы, `new`, приведение, `is`/`as`, `.`/`[]`/`()`, пре/постфиксы + тесты

## Этап 3 — Версии CS2–CS14

- [ ] T3.1 CS2: дженерики + constraints, `var`, анонимные методы, `partial`, sealed override
- [ ] T3.2 CS3: лямбды, auto-свойства, object/collection initializers, extension methods, анонимные типы, LINQ-запросы
- [ ] T3.3 CS4: `dynamic`, именованные/опциональные аргументы, `params`-массив, constraint `new`
- [ ] T3.4 CS5: `async`/`await`
- [ ] T3.5 CS6: интерполяция, `?.`, expression-bodied члены, `nameof`, binary literals
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
