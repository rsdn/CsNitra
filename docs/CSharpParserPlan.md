# Дорожная карта: парсер C# 1–14 на CsNitra

## Подход

- Грамматика = текстовые файлы CsNitra, по одному файлу на версию C#: `Cs1.grammar`, `Cs2.grammar`, …, `Cs14.grammar`.
- Грамматика C# 14 = слияние файлов 1→14 по возрастанию; каждая новая версия расширяет предыдущие: новые правила + новые альтернативы существующих правил (в т.ч. TDOPP-правил вроде `Expression`).
- Источник «реальной» грамматики: Roslyn (`C:\RSDN\roslyn\`) — `src/Compilers/CSharp/Syntax/` (`SyntaxParser.cs` — основной парсер, `TokenList.cs` — таблица токенов), тесты `tests/Compilers/CSharp/`. Версионность в Roslyn задана проверками `CSharpVersion` — по ним и разбиваем на версии.
- Сначала текст грамматики парсится в рантайме (`CsNitraParser` + `BuildFromAst` — путь уже работает, покрыт тестами). Перенос сборки грамматики в Roslyn source generator (текст → `Rule` на этапе компиляции) — завершающая итерация.
- Язык текстовой грамматики расширяем только когда версии это потребует; расширение метациркулярно: `CsNitraParser` + AST + visitor + `RuleGenerator` + TypeChecker + тесты (паттерн `ShouldParseItself`).
- Размер задачи = одна сессия субагента. Исследование кодовых баз — только через субагента с roslyn MCP.

## Этап 0 — Инфраструктура

| # | Задача | Результат |
|---|--------|-----------|
| T0.1 | Проект `Parsers/CSharp/CSharpGrammar` (тексты грамматики + код), добавить в `Nitra.sln` | Оболочка `CSharpParser`: собирает `Parser` из текста грамматики |
| T0.2 | Тестовый проект `Tests/CSharpGrammarTests` (MSTest, net8.0), зависимость от CSharpGrammar | Зелёный smoke-тест |
| T0.3 | Слияние версий: повторное имя правила в более новом файле = добавление альтернатив, а не замена | Изменения в CsNitraGrammar (RuleGenerator/TypeChecker) + метациркулярные тесты |

## Этап 1 — C# 1.0: compilation unit → типы

| # | Задача | Результат |
|---|--------|-----------|
| T1.1 | (Исследование, субагент + roslyn MCP) Карта Roslyn: файлы парсера, паттерны проверок `CSharpVersion`, где лежат синтаксические тесты (корректный код и разбитый код). Граница: до уровня типов | `docs/RoslynGrammarMap.md` |
| T1.2 | Терминалы C#: идентификатор, числа, char/строки, пунктуация, trivia/комментарии | + тесты |
| T1.3 | Грамматика: compilation unit, `using`, `extern alias`, `namespace` (block), namespace-члены: class/struct/enum/interface/delegate (заголовки, без тел) | + тесты (примеры из Roslyn + простые кейсы) |
| T1.4 | Грамматика: типы C# 1.0 (преопределённые, квалифицированные, массивы, указатели) | + тесты |

## Этап 2 — C# 1.0: члены и тела

| # | Задача | Результат |
|---|--------|-----------|
| T2.1 | Члены: поля, свойства, методы, конструкторы, деструкторы, операторы, индексаторы, события, модификаторы, атрибуты | + тесты |
| T2.2 | Операторы/выражения-вызовы: if/while/for/foreach/switch/try/using/lock/check/return/goto/throw/block/empty | + тесты |
| T2.3 | Выражения C# 1: таблица приоритетов (TDOPP), литералы, `new`, приведение, `is`/`as`, `.`/`[]`/`()`, пре/постфиксы | + тесты |

## Этап 3 — Версии CS2–CS14 (по одной итерации на версию: исследование → файл `CsN.grammar` → тесты зелёные)

- **T3.1 CS2**: дженерики + constraints, `var`, анонимные методы (`delegate`), `partial`, sealed override
- **T3.2 CS3**: лямбды, auto-свойства, object/collection initializers, extension methods, анонимные типы, LINQ-запросы
- **T3.3 CS4**: `dynamic`, именованные/опциональные аргументы, `params`-массив, constraint `new`
- **T3.4 CS5**: `async`/`await`
- **T3.5 CS6**: интерполяция строк, `?.`, expression-bodied члены, `nameof`, binary literals
- **T3.6 CS7**: кортежи, pattern matching (declaration, guard `when`), локальные функции, `out var`, `ref`-возврат/локальные, разделители цифр, `throw` как выражение, `ref readonly`
- **T3.7 CS7.1–7.2**: `default`, `in`, `ref struct`, type/constant patterns
- **T3.8 CS8**: switch expressions, using declarations, range/index `..`/`^`, `??=`, NRT-аннотации, default interface members
- **T3.9 CS9**: records, `with {}`, init-only, top-level statements, static abstract в интерфейсах
- **T3.10 CS10**: file-scoped `namespace`, `global using`
- **T3.11 CS11**: raw-строки `"""`, generic attributes, `required`, `params Span<T>`
- **T3.12 CS12**: primary constructors (классы), collection expressions `[]`, list patterns, `not`/`and`/`or`
- **T3.13 CS13**: extension members (`T.X`), `field`, `event field`
- **T3.14 CS14**: `ref`-поля + остаток фич по данным T1.1

## Этап 4 — Закаливание

| # | Задача | Результат |
|---|--------|-----------|
| T4.1 | Тесты разбитого кода: найти recovery-тесты Roslyn, адаптировать, прогнать; согласовать с `ExtensibleParser/Recovery` (в работе) | Зелёные recovery-тесты |
| T4.2 | Производительность: бенчмарк на больших файлах, анализ горячих точек (TDOPP, мемоизация) | Отчёт, оптимизации |
| T4.3 | Перенос сборки грамматики в Roslyn source generator (текст → `Rule` на этапе компиляции) | Скорость сборки + ранние ошибки грамматики |

## Риски фреймворка (решаются точечно, когда упрёмся)

- Препроцессор `#if/#define/…` — отдельный текстовый проход до парсинга (как в Roslyn); маппинг позиций для диагностики.
- TypeChecker WIP (2 пропущенных теста) — диагностика текстов грамматики.
- Идентификаторы: в regex-движке нет combining-символов и `\u`-экранирований — при необходимости кастомный `Terminal`.
- Токены `?.`/`..` с допустимым whitespace между знаками — regex-терминалы вида `\?\s*[.[`.
- Ключевые слова vs идентификаторы — паттерн на уровне правила: `!KeywordSet Identifier` (не терминал).
- Raw-строки с 4+ кавычками — кастомный `Terminal`.
