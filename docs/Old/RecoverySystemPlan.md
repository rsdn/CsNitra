# План реализации системы восстановления после ошибок

## Введение

Данный документ описывает архитектуру и план поэтапной реализации системы восстановления после ошибок для CsNitra-парсера. Система сочетает автоматические алгоритмы (follow-set, parse stack, cost-based выбор) с декларативными аннотациями грамматики для пользовательских расширений.

### Ключевые отличия от Roslyn и их последствия

| Отличие                         | Последствие для recovery                                                                  |
| ------------------------------- | ----------------------------------------------------------------------------------------- |
| Нондетерминизм + longest-match  | Recovery должен генерировать несколько альтернатив и выбирать по cost, а не по порядку    |
| Предикаты `&` / `!`             | Автор языка может задавать терминаторы через предикаты, не полагаясь только на follow-set |
| Языково-независимый             | Все стратегии должны быть параметризуемы через аннотации грамматики                       |
| Без лексера (терминалы = regex) | Follow-set работает на уровне терминальных правил, а не token kind                        |
| Trivia на уровне символов       | Пропуск текста возможен с символьной точностью                                            |

### Принципы

1. **Ничего не теряется** — каждый символ входного текста представлен в дереве.
2. **Инкрементальный подход** — парсинг итеративен. Каждый проход доходит дальше предыдущего: успешный префикс кэшируется в memo, ошибка обнаруживается глубже, recovery расширяет разобранную область. Цикл завершается, когда парсинг доходит до конца строки.
3. **Progressive complexity** — от простых механизмов (missing token injection) к сложным (multi-path exploration с cost).
4. **Каждый этап тестируется** — каждая фаза имеет свой набор unit-тестов.

### Интегрированные замечания от LLM-ревью

План учитывает 11 критических замечаний, интегрированных в соответствующие разделы (помечены `[ЗН-#]`):

| # | Замечание | Решение |
|---|-----------|---------|
| 1 | Memo-инъекция терминалов не сработает | Отдельный `_terminalMemo` для кэша результатов `TryMatch` |
| 2 | Partial memo привязан к `_recoverySkipPos` | Отдельный флаг `IsRecoveryPatch` на Partial, проверка всегда |
| 3 | Инвариант прогресса при пустом токене | `MaxRecoveryAttemptsPerPosition`, проверка сдвига позиции |
| 4 | Смешение trivia и пропущенного текста | Отдельное поле `SkippedText` в `TerminalNode` |
| 5 | Производительность: struct → классы | Сохранить struct, nullable `ParseContext?` только для Partial |
| 6 | Судьба существующего recovery-кода | Явный план миграции: что удаляется, что остаётся |
| 7 | Политика очистки memo | Чёткий алгоритм `CleanMemoForRecovery` с псевдокодом |
| 8 | FollowSet на regex-терминалах | Кэш `TryMatch` результатов, `RecoveryOptions.Terminators` |
| 9 | Стратегия «замена токена» | Комбинация skip + insert, cost = 2 |
| 10 | Приоритет пользовательских аннотаций | Пользовательские аннотации имеют приоритет, автоматические не применяются при конфликте |
| 11 | Проблема предикатов | Флаг `isSpeculative`, сохранение/восстановление `ErrorPos` |

---

## Фаза 0: Исследование и инфраструктура диагностики

**Цель:** Создать инфраструктуру для сбора и отладки информации о восстановлении.

### 0.1. Расширение FollowSetCalculator

Текущий `FollowSetCalculator` имеет следующие ограничения:

- Хардкод `Module` как стартового символа
- Не обрабатывает `SeparatedList`, `ReqRef` (TDOPP), `OneOrMany`, `ZeroOrMany`, `Optional`
- Не учитывает вложенность правил (follow-set внутреннего правила не включает терминаторы внешних)

**Что делать:**

1. **Переписать `FlattenRule`** — добавить обработку всех типов правил:
   
   - `SeparatedList` → First(element) ∪ First(separator)
   - `ReqRef` → First(referenced rule)
   - `OneOrMany` / `ZeroOrMany` → First(element) ∪ {ε}
   - `Optional` → First(element) ∪ {ε}
   - `OftenMissed` → First(element) ∪ {ε}

2. **Terminal identity** — идентификация терминалов:
   - `Literal` — по строковому `Value`.
   - Остальные (`RecoveryTerminal`, сгенерированные терминалы) — по инстансу (синглтоны).

3. **Конфигурируемый start symbol** — принимать список стартовых символов в конструкторе.

4. **Nested follow-sets** — вычислять follow-set с учётом вложенности. Если правило `Block` содержит `ZeroOrMany(Statement)`, то follow-set `Statement` должен включать терминаторы `Block` (например, `}`).

**Файлы:** `ExtensibleParser/FollowSetCalculator.cs`

**Тесты:** `Tests/ParserTests/Recovery/FollowSetTests.cs`

- `Test_FirstSet_SimpleTerminal`
- `Test_FirstSet_WithOptional`
- `Test_FirstSet_WithSeparatedList`
- `Test_FollowSet_NestedRules` — follow-set внутреннего правила включает терминаторы внешнего
- `Test_FollowSet_TDOPP` — корректная обработка `ReqRef`

---

### 0.2. Контекст восстановления в Result.Partial [ЗН-5]

Вместо отдельного стека восстановления, контекст парсинга будет храниться непосредственно в `Result.Partial` и таблице мемоизации. Это позволяет не только восстанавливать стек в точке падения, но и анализировать альтернативные пути разбора, которые уже были вычислены и закэшированы.

**Архитектура: обогащение `Result.Partial` без потери производительности.**

`Result.Partial` должен нести достаточно информации, чтобы recovery engine мог:
1. Понять, где именно произошёл сбой (внутри цикла, в конкретном элементе `Seq`, в альтернативе).
2. Определить, какие терминалы ожидались на этой позиции.
3. Восстановить стек разбора из цепочки partial-результатов в memo.

**[ЗН-5] Сохранение `readonly record struct` с nullable `ParseContext?`:**

Для минимизации аллокаций `Result` остаётся `readonly record struct`. Контекст добавляется как nullable-ссылка, которая аллоцируется только для Partial-результатов:

```csharp
public readonly record struct Result
{
    public enum Kind { Success, Partial, Failure }

    public readonly Kind ResultKind;
    public readonly int NewPos;
    public readonly int MaxFailPos;
    internal readonly ISyntaxNode? Node;
    internal readonly ParseContext? Context; // [ЗН-5] nullable, только для Partial

    public bool IsSuccess => ResultKind == Kind.Success;

    public static Result Success(ISyntaxNode node, int newPos, int maxFailPos) =>
        new(Kind.Success, node, newPos, maxFailPos, context: null);

    public static Result Partial(ISyntaxNode partialTree, int parsedUpTo,
        int maxFailPos, ParseContext context) =>
        new(Kind.Partial, partialTree, parsedUpTo, maxFailPos, context);

    public static Result Failure(int failPos) =>
        new(Kind.Failure, null, -1, failPos, context: null);
}

public record ParseContext(
    string RuleName,           // текущее правило
    ParseLocation Location,    // где в правиле: элемент Seq, итерация цикла
    Terminal[] Expected,       // ожидаемые терминалы
    RecoveryOptions? Options   // пользовательская аннотация (Фаза 4)
);

public abstract record ParseLocation;
public sealed record SeqLocation(int ElementIndex) : ParseLocation;
public sealed record LoopLocation(string LoopKind, int Iteration) : ParseLocation;
public sealed record PrefixLocation(int PrefixIndex) : ParseLocation;
```

**Восстановление стека из memo:**

При падении парсера, recovery engine собирает все `Partial` из `_partialMemo` и `_memo`, которые заканчиваются на `ErrorPos` или находятся "по пути" к ней. Сортируя их по позиции и глубине, мы реконструируем полный стек разбора на момент ошибки, включая альтернативные пути.

**[ЗН-3] Класс `RecoveryStackReconstructor`:**

Класс инкапсулирует логику реконструкции стека разбора из мемоизационных таблиц. Используется в Фазе 1.1 для получения стека перед генерацией патчей.

```csharp
public static class RecoveryStackReconstructor
{
    public static IReadOnlyList<ParseContext> ReconstructFromMemo(
        Dictionary<(int pos, string rule, int precedence), Result> memo,
        Dictionary<(int pos, string rule, int precedence), Result> partialMemo,
        int errorPos)
    {
        // 1. Собираем все Partial, которые заканчиваются на errorPos или находятся "до" неё
        var partials = partialMemo
            .Where(kvp => kvp.Value.ResultKind == Result.Kind.Partial)
            .Select(kvp => kvp.Value)
            .Where(p => p.NewPos <= errorPos)
            .OrderBy(p => p.NewPos)
            .ThenByDescending(p => p.Context?.Location.GetDepth() ?? 0)
            .ToList();

        // 2. Реконструируем стек контекстов
        var stack = new List<ParseContext>();
        foreach (var partial in partials)
        {
            if (partial.Context != null)
                stack.Add(partial.Context);
        }

        // 3. Добавляем контекст из memo для правил, которые упали
        var failedRules = memo
            .Where(kvp => kvp.Value.ResultKind == Result.Kind.Failure && kvp.Value.MaxFailPos == errorPos)
            .Select(kvp => new ParseContext(
                RuleName: kvp.Key.rule,
                Location: new SeqLocation(0), // приблизительно
                Expected: [], // будет заполнен из _expected
                Options: null))
            .ToList();

        stack.AddRange(failedRules);
        return stack;
    }
}
```

**Файлы:**

- `ExtensibleParser/Result.cs` — добавление `ParseContext?` в struct
- `ExtensibleParser/Recovery/ParseContext.cs`
- `ExtensibleParser/Recovery/RecoveryStackReconstructor.cs` — **[новый файл]**
- Модификация `ExtensibleParser/Parser.cs` — создание `Partial` с `Context`

**Тесты:** `Tests/ParserTests/Recovery/ParseContextTests.cs`

- `Test_Context_InPartial` — контекст сохраняется в partial-результате
- `Test_Stack_Reconstruction` — стек восстанавливается из memo
- `Test_Expected_Terminals` — ожидаемые терминалы корректны
- `Test_Nested_Contexts` — вложенные контексты (цикл внутри Seq)
- `Test_NoAllocationForSuccess` — Success/Failure не аллоцируют ParseContext
- `Test_StackReconstructor_Accuracy` — точность реконструкции стека

---

### 0.3. Представление ошибок в дереве [ЗН-4]

Ошибки не создают новых типов узлов. Они встраиваются в существующие `TerminalNode`:

**Пропущенный токен (Missing Token):**
Создаётся пустой `TerminalNode` с `ContentLength = 0`. `StartPos == EndPos == Position`. Без флага `IsRecovery` (выглядит как обычный токен для visitor-а, но с нулевой длиной).

**[ЗН-4] Пропущенный текст — отдельная хэш-таблица, без строк в TerminalNode:**

Не добавляем строки в `TerminalNode`. Вместо этого — внешняя хэш-таблица в `Parser`:

```csharp
public class Parser(Terminal trivia, Log? log = null)
{
    // ... существующие поля ...
    
    // [ЗН-4] Хэш-таблица для skipped text: TerminalNode -> (StartPos, Length)
    // Ключ — сам TerminalNode (reference equality), значение — позиция и длина пропущенного текста
    private readonly Dictionary<TerminalNode, (int StartPos, int Length)> _skippedTextMap = new();
    
    // Восстанавливает пропущенный текст по узлу
    internal string? GetSkippedText(TerminalNode node, string input)
    {
        if (_skippedTextMap.TryGetValue(node, out var info))
            return input.Substring(info.StartPos, info.Length);
        return null;
    }
    
    // Регистрирует skipped text для узла (вызывается при генерации memo-патча)
    internal void RegisterSkippedText(TerminalNode node, int startPos, int length)
    {
        _skippedTextMap[node] = (startPos, length);
    }
}
```

`TerminalNode` остаётся без изменений (без `SkippedText`, без `IsRecovery`):

```csharp
public record TerminalNode(
    string Kind,
    int StartPos,
    int EndPos,
    int ContentLength
) : Node(Kind, StartPos, EndPos);
```

**API для visitor-а:**

```csharp
public static class RecoveryTreeExtensions
{
    // Получить пропущенный текст через Parser
    public static string? GetSkippedText(this TerminalNode node, Parser parser, string input)
        => parser.GetSkippedText(node, input);

    // Проверить, является ли узел recovery-узлом (есть ли skipped text)
    public static bool IsRecovery(this TerminalNode node, Parser parser)
        => parser._skippedTextMap.ContainsKey(node);

    // Получить все recovery-узлы в поддереве
    public static IEnumerable<ISyntaxNode> GetAllRecoveryNodes(this ISyntaxNode node, Parser parser);
}
```

**IsRecovery как функция через хэш-таблицу:**
Вместо поля `IsRecovery` — проверка наличия в `_skippedTextMap`. Это избавляет от лишнего поля в каждом терминале.

**Файлы:**
- `ExtensibleParser/Recovery/RecoveryDiagnostic.cs`
- `ExtensibleParser/Parser.cs` — добавление `_skippedTextMap`
- `ExtensibleParser/Recovery/RecoveryTreeExtensions.cs` — extension методы с `Parser` параметром

**Тесты:** `Tests/ParserTests/Recovery/ErrorRepresentationTests.cs`
- `Test_MissingToken_ZeroLength`
- `Test_SkippedText_InExternalMap` — пропущенный текст во внешней хэш-таблице
- `Test_EndOfInput_FollowSetCompletion`

---

## Фаза 1: Мемо-инъекция и итеративное восстановление

**Цель:** Реализовать систему, где recovery engine работает **между итерациями парсера**, модифицируя таблицы мемоизации. Парсер не знает о recovery — он просто читает из memo.

### 1.1. Алгоритм итеративного восстановления [ЗН-1,2,3,7,11]

**Ключевая идея:** memoization table — это точка инъекции recovery. Когда парсинг падает, recovery engine анализирует контекст ошибки (из `Result.Partial.Context`) и генерирует memo-патчи. Патчи вставляют терминалы и модифицируют `Partial`-результаты, чтобы парсер мог продолжить разбор с нужной точки (следующий элемент Seq, следующая итерация цикла).

**[ЗН-1] Мемоизация терминалов:**

`ParseTerminal` не обращается к `_memo`. Решение: отдельный кэш для результатов `TryMatch`:

```csharp
public class Parser(Terminal trivia, Log? log = null)
{
    // Существующее:
    private readonly Dictionary<(int pos, string rule, int precedence), Result> _memo = new();
    private readonly Dictionary<(int pos, string rule, int precedence), Result> _partialMemo = new();

    // [ЗН-1] Новый кэш для результатов TryMatch терминалов
private readonly Dictionary<(int pos, Terminal terminal), int> _terminalMemo = new();

    // [ЗН-2] Делегат для регистрации skipped text во внешнюю хэш-таблицу
    private readonly Action<TerminalNode, int, int> _registerSkippedText;

    // [ЗН-1] Флаг: активен ли режим recovery
    private bool _isRecoveryMode = false;

    public Result Parse(string input, string startRule, out int triviaLength, int startPos = 0)
    {
        // ... инициализация ...
        _terminalMemo.Clear();
        _skippedTextMap.Clear();  // [ЗН-4] очистка skipped text map

        // [ЗН-2] Инициализируем recovery engine с делегатом для регистрации skipped text
        _recoveryEngine = new RecoveryEngine(this, RegisterSkippedText);
            _skippedTextMap.Clear();  // [ЗН-4] очистка skipped text map

        for (int iteration = 0; ; iteration++)
        {
            var oldErrorPos = ErrorPos;

            // === Шаг: парсинг от начала (ускоряется за счёт memo) ===
            var parseResult = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);

            // === Успех: дошли до конца строки ===
            if (parseResult.TryGetSuccess(out _, out var newPos) && newPos == input.Length)
                return parseResult;

            // === Ошибка: ErrorPos не продвинулся → recovery не помог ===
            if (ErrorPos <= oldErrorPos)
            {
                ErrorInfo = new FatalError(input, ErrorPos, ...);
                return parseResult;
            }

            // === Recovery: ErrorPos продвинулся → пытаемся восстановить ===
            var parseStack = RecoveryStackReconstructor.ReconstructFromMemo(_memo, _partialMemo, ErrorPos);

            var recoveryContext = new RecoveryContextSnapshot(
                errorPos: ErrorPos,
                stack: parseStack,
                expected: _expected.ToArray(),
                memo: _memo,
                partialMemo: _partialMemo
            );

            // Recovery engine генерирует модификации
            var patchSets = _recoveryEngine.GeneratePatchSets(recoveryContext, input);

            // [ЗН-10] Приоритет пользовательских аннотаций
            var selected = SelectBestPatchSet(patchSets);

            // Применяем патчи
            foreach (var patch in selected.Patches)
            {
                if (patch.IsTerminalPatch)
                    _terminalMemo[patch.TerminalKey] = patch.TerminalValue; // [ЗН-1]
                else
                    _memo[patch.Key] = patch.Value;
            }

            _recoveryDiagnostics.AddRange(selected.Diagnostics);

            // [ЗН-7] Очистка memo между итерациями
            CleanMemoForRecovery(ErrorPos, currentStartPos);

            // Включаем режим recovery для следующей итерации
            _isRecoveryMode = true;
        }
    }
}
```

**[ЗН-1] Модификация `ParseTerminal` — проверка `_terminalMemo`:**

```csharp
private Result ParseTerminal(Terminal terminal, int startPos, string input)
{
    // [ЗН-1] Проверяем кэш терминалов
    var terminalKey = (startPos, terminal);
    if (_terminalMemo.TryGetValue(terminalKey, out var cachedMatch))
    {
        if (cachedMatch == -1)
            return Result.Failure(startPos);  // кэшированный mismatch

        // Кэшированный match: создаём TerminalNode
        var currentPos = startPos + cachedMatch;
        var triviaLength = Trivia.TryMatch(input, currentPos);
        if (triviaLength > 0) currentPos += triviaLength;

        return Result.Success(
            new TerminalNode(terminal.Kind, startPos, currentPos, cachedMatch),
            currentPos, currentPos);
    }

    // Обычный TryMatch
    var contentLength = terminal.TryMatch(input, startPos);
    if (contentLength < 0)
    {
        _terminalMemo[terminalKey] = -1;  // кэшируем mismatch
        // ... existing error handling ...
        return Result.Failure(startPos);
    }

    _terminalMemo[terminalKey] = contentLength;  // кэшируем match
    // ... existing success handling ...
}
```

**[ЗН-2] Активация Partial memo вне зависимости от `_recoverySkipPos`:**

Существующий код проверяет `_partialMemo` только при `startPos == _recoverySkipPos`. Решение: добавить флаг `IsRecoveryPatch` на Partial и проверять всегда:

```csharp
// В ParseRule:
if (!isRecoveryPos && _partialMemo.TryGetValue(memoKey, out var partialCached))
{
    // [ЗН-2] Проверяем, является ли это recovery-патчем
    if (partialCached.Context?.IsRecoveryPatch == true)
    {
        Log($"Found recovery-patch partial memo: {memoKey}", LogImportance.High);
        // Partial уже модифицирован recovery engine для continuation
        return _memo[memoKey] = partialCached;
    }
}
```

**[ЗН-11] Изоляция предикатов:**

`AndPredicate` и `NotPredicate` делают спекулятивный разбор, который не должен влиять на `ErrorPos` и `_expected`:

```csharp
private Result ParseAndPredicate(AndPredicate a, int startPos, string input)
{
    // [ЗН-11] Сохраняем состояние ошибки
    var savedErrorPos = ErrorPos;
    var savedExpected = _expected.ToList();

    var predicateResult = ParseAlternative(a.PredicateRule, startPos, input);

    // [ЗН-11] Восстанавливаем состояние ошибки
    ErrorPos = savedErrorPos;
    _expected.Clear();
    _expected.AddRange(savedExpected);

    if (predicateResult.IsSuccess)
        return Result.Success(new PredicateNode(a.Kind, startPos, startPos), startPos, predicateResult.MaxFailPos);
    else
        return Result.Failure(startPos);
}

private Result ParseNotPredicate(NotPredicate predicate, int startPos, string input)
{
    // [ЗН-11] Аналогично: сохраняем/восстанавливаем
    var savedErrorPos = ErrorPos;
    var savedExpected = _expected.ToList();

    var predicateResult = ParseAlternative(predicate.PredicateRule, startPos, input);

    ErrorPos = savedErrorPos;
    _expected.Clear();
    _expected.AddRange(savedExpected);

    if (!predicateResult.IsSuccess)
        return Result.Success(new PredicateNode(predicate.Kind, startPos, startPos), startPos, predicateResult.MaxFailPos);
    else
        return Result.Failure(startPos);
}
```

**[ЗН-7] Алгоритм очистки memo:**

```csharp
// [ЗН-7] Чёткая политика очистки memo между итерациями
private void CleanMemoForRecovery(int errorPos, int currentStartPos)
{
    var keysToRemove = new List<(int pos, string rule, int precedence)>();

    foreach (var kvp in _memo)
    {
        var key = kvp.Key;
        var result = kvp.Value;

        // Удаляем Failure, которые заканчиваются на ErrorPos
        if (result.ResultKind == Result.Kind.Failure && result.MaxFailPos == errorPos)
            keysToRemove.Add(key);

        // Удаляем Failure на currentStartPos
        if (result.ResultKind == Result.Kind.Failure && key.pos == currentStartPos)
            keysToRemove.Add(key);

        // Удаляем Success, которые заканчиваются на ErrorPos
        // (они были частью пути, который привёл к ошибке)
        if (result.ResultKind == Result.Kind.Success && result.NewPos == errorPos)
            keysToRemove.Add(key);

        // Удаляем Partial, которые заканчиваются на ErrorPos
        if (result.ResultKind == Result.Kind.Partial && result.NewPos == errorPos)
            keysToRemove.Add(key);
    }

    foreach (var key in keysToRemove.Distinct())
        _memo.Remove(key);

    // Очищаем _partialMemo полностью (пересоздаётся в следующем проходе)
    _partialMemo.Clear();

    // [ЗН-1] Очищаем кэш терминалов на ErrorPos
    var terminalKeysToRemove = _terminalMemo
        .Where(kvp => kvp.Key.pos == errorPos)
        .Select(kvp => kvp.Key)
        .ToList();
    foreach (var key in terminalKeysToRemove)
        _terminalMemo.Remove(key);
}
```

**[ЗН-3] Инвариант прогресса при вставке пустого токена:**

Вставка пустого `TerminalNode` (ContentLength=0) не двигает позицию. Решение: проверять, что модификация Partial гарантированно сдвигает позицию хотя бы на 1 символ:

```csharp
// В RecoveryEngine.GeneratePatchSets:
foreach (var candidate in allCandidates)
{
    // [ЗН-3] Проверяем, что патч гарантированно сдвигает позицию
    var newPosition = GetNewPositionAfterPatch(candidate, input);
    if (newPosition <= errorPos)
        continue;  // Отбрасываем патч, который не сдвигает позицию

    // [ЗН-3] Проверяем лимит попыток на позицию
    var attempts = _recoveryAttemptsPerPosition.GetValueOrDefault(errorPos, 0);
    if (attempts >= MaxRecoveryAttemptsPerPosition)
        continue;  // Превышен лимит

    patchSets.Add(candidate);
}
```

**[ЗН-8] FollowSet на regex-терминалах — кэширование TryMatch:**

```csharp
// В SkipAheadStrategy:
// [ЗН-8] Кэш результатов TryMatch для skip-ahead
private readonly Dictionary<(int pos, Terminal terminal), int> _tryMatchCache = new();

int CachedTryMatch(Terminal terminal, string input, int pos)
{
    var key = (pos, terminal);
    if (!_tryMatchCache.TryGetValue(key, out var result))
    {
        result = terminal.TryMatch(input, pos);
        _tryMatchCache[key] = result;
    }
    return result;
}
```

**Итерация восстановления:**

```
1. Падение → получаем ErrorPos, ErrorInfo, FollowSet
2. Восстановление стека разбора из Result.Partial в memo
3. Анализ: какие терминалы ожидались, где именно упали (Seq/цикл/prefix)
4. Генерация патчей:
   - Для каждой стратегии (вставка / пропуск):
     a. Создаём TerminalNode (пустой или с пропущенным текстом)
     b. Патчим _terminalMemo: вставляем Success для терминала [ЗН-1]
     c. Модифицируем Partial: продвигаем контекст к следующей точке
5. Очистка memo: удаляем записи, мешающие recovery [ЗН-7]
6. Запуск парсинга заново:
   - Парсер входит в те же колстеки
   - Находит Success в _terminalMemo → продолжает [ЗН-1]
   - Находит модифицированный Partial → продолжает с нужной точки [ЗН-2]
7. Успешное завершение (до конца строки) или падение на новой позиции

Инвариант: на каждой итерации парсер продвигается хотя бы на один символ вперёд [ЗН-3].
```

**[ЗН-6] Миграция существующего recovery-кода:**

| Код | Статус | Действие |
|-----|--------|----------|
| `ContinueFromPartial` | **Удаляется** | Заменяется на модификацию Partial через memo-патчи |
| `TryRecoverFromPartial` | **Удаляется** | Заменяется на RecoveryEngine.GeneratePatchSets |
| `MergeWithPartialTree` | **Удаляется** | Больше не нужна — Partial модифицируются, а не объединяются |
| `RecoveryPrefix` / `RecoveryPostfix` в `TdoppRule` | **Удаляется** | Заменяется на `_terminalMemo` + модифицированные Partial |
| `BuildTdoppRules` (recovery-логика) | **Упрощается** | Оставляет только prefix/postfix разделение, убирает recovery-ветки |
| `OftenMissed` | **Остаётся** | Частный случай: автоматически генерирует memo-патч для вставки |
| `RecoveryTerminal` | **Остаётся** | Терминал для поглощения неожиданного текста |
| `EmptyTerminal` | **Остаётся** | Пустой терминал для вставки |
| `_recoverySkipPos` | **Удаляется** | Заменяется на `_isRecoveryMode` + `IsRecoveryPatch` в Context |
| Цикл `for (int i = 0; ; i++)` | **Остаётся** | Но с новой логикой: memo-патчи вместо перезапуска |

**План миграции:**

1. **Фаза 0.4** (новая подфаза): Добавить `_terminalMemo`, `_isRecoveryMode`, `IsRecoveryPatch` в Context
2. **Фаза 1.1**: Реализовать новый цикл с memo-патчами, оставить старый код за флагом `UseNewRecovery`
3. **Фаза 1.4**: Отключить старый код (`ContinueFromPartial`, `TryRecoverFromPartial`, `MergeWithPartialTree`)
4. **Фаза 2**: Упростить `BuildTdoppRules`, убрать recovery-ветки
5. **Фаза 3**: Удалить `_recoverySkipPos`, полностью перейти на новую архитектуру

**Файлы:**

- `ExtensibleParser/Recovery/RecoveryContextSnapshot.cs` — снимок контекста ошибки
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — генерация memo-патчей
- `ExtensibleParser/Recovery/MemoPatch.cs` — патч таблицы мемоизации
- `ExtensibleParser/Recovery/PartialModifier.cs` — модификация Partial для continuation
- Модификация `ExtensibleParser/Parser.cs` — основной цикл, `_terminalMemo`, `_isRecoveryMode`, изоляция предикатов

**Тесты:** `Tests/ParserTests/Recovery/IterativeRecoveryTests.cs`

- `Test_SingleError_SingleRecovery` — одна ошибка, один патч, два прохода
- `Test_MultipleErrors_MultipleRecoveries` — несколько ошибок, несколько проходов
- `Test_TerminalMemo_Cached` — _terminalMemo работает
- `Test_PartialRecoveryPatch_Activated` — recovery-patch Partial активирован вне _recoverySkipPos
- `Test_Predicate_Isolated` — предикаты не влияют на ErrorPos
- `Test_MemoCleanup_Correct` — memo очищается правильно
- `Test_ProgressInvariant` — ErrorPos всегда растёт
- `Test_MaxRecoveryAttempts` — лимит попыток на позицию

**Почему не single-pass:**

При longest-match парсер не может знать, что конструкция сломана, пока не попробует все альтернативы. Пример:

```
int foo() { int x;
int bar() { return 0; }
```

На позиции `int bar` парсер внутри `Expr` пробует альтернативы: `Number` — нет, `Ident` — матчится `int`, дальше пытается продолжить как выражение и падает. Без полного перебора альтернатив мы не знаем, что `int bar()` — это начало новой функции, а не часть выражения. Поэтому итеративный цикл необходим: первый проход обнаруживает ошибку, второй — с модифицированным memo — корректно разбирает `int bar()` как функцию.

---

### 1.2. Стратегия 1: Вставка ожидаемого токена

Если ожидался терминал на позиции `pos`, а его нет — вписать в memo `Success` с пустым `TerminalNode` и модифицировать `Partial`, чтобы парсер продолжил разбор со следующего элемента.

**Алгоритм в RecoveryEngine:**

```csharp
foreach (var partial in snapshot.PartialsAt(ErrorPos))
{
    var ctx = partial.Context;
    var pos = ErrorPos;

    foreach (var terminal in ctx.Expected)
    {
        // 1. Патч: вставляем пустой терминал в memo
        var missingNode = new TerminalNode(terminal.Kind, pos, pos, ContentLength: 0);
        var terminalPatch = new MemoPatch(
            Key: (pos, terminal.Kind, 0),
            Value: new Success(new TerminalNode(terminal.Kind, pos, pos, 0)),
            Diagnostic: new RecoveryDiagnostic(pos, 0, $"Expected {terminal.Kind}", [terminal]),
            Cost: 1
        );

        // 2. Патч: модифицируем Partial — продвигаем контекст
        var modifiedPartial = ModifyPartialForContinuation(partial, ctx, pos);
        var partialPatch = new MemoPatch(
            Key: (partial.Key.pos, partial.Key.rule, partial.Key.precedence),
            Value: modifiedPartial,
            Diagnostic: terminalPatch.Diagnostic,
            Cost: 0  // модификация Partial бесплатна
        );

        patches.Add(new RecoveryPatchSet([terminalPatch, partialPatch]));
    }
}
```

**`ModifyPartialForContinuation`** — модифицирует `Partial` в зависимости от контекста:

```csharp
Result ModifyPartialForContinuation(Partial partial, ParseContext ctx, int pos)
{
    return ctx.Location switch
    {
        // Упали на элементе Seq → продвигаем к следующему элементу
        SeqLocation { ElementIndex: idx } =>
            new Partial(partial.PartialTree,
                new ParseContext(ctx.RuleName,
                    new SeqLocation(idx + 1), // следующий элемент
                    GetExpectedForNextElement(ctx, idx + 1),
                    ctx.Options),
                partial.MaxFailPos),

        // Упали в цикле → продолжаем с новой итерации
        LoopLocation { Iteration: n } =>
            new Partial(partial.PartialTree,
                new ParseContext(ctx.RuleName,
                    new LoopLocation(ctx.Location.LoopKind, n + 1),
                    GetExpectedForLoopIteration(ctx),
                    ctx.Options),
                partial.MaxFailPos),

        // Упали в prefix TDOPP → переходим к postfix
        PrefixLocation { PrefixIndex: idx } =>
            new Partial(partial.PartialTree,
                new ParseContext(ctx.RuleName,
                    new PostfixLocation(idx),
                    GetExpectedForPostfix(ctx, idx),
                    ctx.Options),
                partial.MaxFailPos),

        _ => partial  // не модифицируем
    };
}
```

**Важно:** вставка должна происходить только когда:

- Текущий токен не совпадает с ожидаемым
- Следующий токен не может быть началом ожидаемой конструкции
- Вставка не приводит к зацикливанию (инвариант прогресса)

**Файлы:**

- `ExtensibleParser/Recovery/Strategies/InsertTokenStrategy.cs`
- `ExtensibleParser/Recovery/PartialModifier.cs`

**Тесты:** `Tests/ParserTests/Recovery/InsertTokenTests.cs`

- `Test_InsertInSeq` — вставка в элементе Seq, continuation к следующему
- `Test_InsertInLoop` — вставка в цикле, continuation к следующей итерации
- `Test_InsertInPrefix` — вставка в prefix TDOPP, continuation к postfix
- `Test_MissingSemicolon` — пропущенная точка с запятой
- `Test_MissingClosingBrace` — пропущенная `}`
- `Test_PartialModified` — Partial модифицирован для continuation

---

### 1.3. Стратегия 2: Пропуск текста

Когда текущий токен не ожидается и вставка missing token не помогает — пропустить текст до первого совпадения с follow-set текущего правила. Пропущенный текст записывается во внешний мап `_skippedTextMap`.

**Алгоритм в RecoveryEngine:**

```csharp
SkipAheadAndPatch(pos, input, parseStack, memo, parser):
    // Агрегируем follow-sets всех уровней стека
    terminators = parseStack.AggregateFollowSets()

    // [ЗН-8] Используем кэш TryMatch
    var tryMatchCache = new Dictionary<(int, Terminal), int>();

    scanPos = pos

    while scanPos < input.Length:
        // Пытаемся матчить каждый терминал из terminators
        foreach terminal in terminators:
            // [ЗН-8] Кэшированный TryMatch
            matchLen = CachedTryMatch(terminal, input, scanPos, tryMatchCache)
            if matchLen >= 0:
                // Нашли терминатор!
                // Находим предыдущий успешный TerminalNode из memo
                var previousNode = FindPreviousTerminalNode(memo, pos);

                if (previousNode != null)
                {
                    // [ЗН-4] Пропущенный текст через RegisterSkippedText
                    var skippedText = input.Substring(pos, scanPos - pos);
                    var skippedNode = new TerminalNode(
                        previousNode.Kind,
                        previousNode.StartPos,
                        EndPos: scanPos, // покрывает пропущенный текст
                        previousNode.ContentLength
                    );

                    // Регистрируем skipped text в Parser
                    parser.RegisterSkippedText(skippedNode, pos, scanPos - pos);

                    // 1. Патч: Success для пропущенного терминала
                    var terminalPatch = new MemoPatch(
                        Key: (pos, currentRuleName, 0),
                        Value: new Success(skippedNode),
                        Diagnostic: new RecoveryDiagnostic(pos, scanPos - pos,
                            $"Skipped to {terminal.Kind}", terminators),
                        Cost: scanPos - pos
                    );

                    // 2. Патч: модифицируем Partial для continuation
                    var partial = FindPartialAt(memo, pos);
                    if (partial != null)
                    {
                        var modifiedPartial = ModifyPartialForContinuation(
                            partial, partial.Context, scanPos);
                        patches.Add(new MemoPatch(
                            Key: partial.Key,
                            Value: modifiedPartial,
                            Diagnostic: terminalPatch.Diagnostic,
                            Cost: 0));
                    }

                    patches.Add(terminalPatch);
                }
                return

        scanPos++

    // Дошли до конца строки без терминатора — патч невозможен
```

**[ЗН-8] Оптимизация:** Поскольку терминалы — regex-паттерны, матчинг каждого терминала на каждой позиции может быть дорогим. Кэшировать результаты TryMatch для позиции. Автор языка может уточнить терминаторы через `RecoveryOptions.Terminators`, чтобы избежать лишних вызовов `TryMatch`.

**Файлы:**

- `ExtensibleParser/Recovery/Strategies/SkipAheadStrategy.cs`
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — интеграция

**Тесты:** `Tests/ParserTests/Recovery/SkipAheadTests.cs`

- `Test_SkipToClosingBrace` — пропуск до `}`
- `Test_SkipToNextStatement` — пропуск до начала следующего стейтмента
- `Test_SkipToEndOfString` — пропуск до конца строки
- `Test_Skip_InExternalMap` — пропущенный текст во внешнем мапе
- `Test_Skip_NestedBraces` — корректная обработка вложенных скобок
- `Test_PartialModifiedAfterSkip` — Partial модифицирован после пропуска

---

### 1.4. OftenMissed — улучшение

Текущий `OftenMissed` работает только при активации через `_recoverySkipPos`. Улучшить, интегрировав с memo-патчами:

**Что менять:**

1. `OftenMissed` помечает терминалы, которые можно вставить через memo-патч. Recovery engine автоматически генерирует патч для `OftenMissed`-терминалов в стеке ошибки (проверяя `IsRecoveryPatch` в контексте).
2. Результат `OftenMissed` — пустой `TerminalNode` с `ContentLength: 0`.

**Файлы:**

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — обработка `OftenMissed` в стеке
- `ExtensibleParser/Rules.cs` — `OftenMissed` (документация)

**Тесты:** `Tests/ParserTests/Recovery/OftenMissedTests.cs`

- `Test_OftenMissed_AtRecoveryPos` — существующее поведение
- `Test_OftenMissed_MemoPatch` — новый: recovery engine генерирует патч для OftenMissed
- `Test_OftenMissed_ProducesEmptyTerminal` — результат — пустой TerminalNode

---

## Фаза 2: Multi-path exploration с cost-based выбором

**Цель:** Recovery engine исследует несколько вариантов патчей и выбирает лучший по cost.

### 2.1. Recovery Candidate и Cost Model

```csharp
public sealed record MemoPatch(
    (int pos, string rule, int precedence) Key,
    Result Result,
    RecoveryDiagnostic Diagnostic,
    int Cost              // стоимость восстановления
);

// Cost вычисляется как:
//   Cost = Insertions * 1 + Deletions * 1 + Replacements * 2
// Где:
//   Insertion — вставка missing token (cost 1)
//   Deletion — пропуск excess token (cost 1 за пропущенный токен)
//   [ЗН-9] Replacement — комбинация skip + insert (cost 2)
```

**Выбор лучшего набора патчей [ЗН-10]:**

```csharp
// В RecoveryEngine.GeneratePatchSets:
var autoPatchSets = new List<RecoveryPatchSet>();
var userPatchSets = new List<RecoveryPatchSet>();

// 1. Автоматические стратегии
autoPatchSets.AddRange(GenerateMissingTokenCandidates(snapshot, input));
autoPatchSets.AddRange(GenerateSkipAheadCandidates(snapshot, input));

// [ЗН-10] 2. Пользовательские стратегии (Фаза 4) — имеют приоритет
userPatchSets.AddRange(GenerateUserCandidates(snapshot, input));

// [ЗН-10] Выбираем лучший набор:
// - Если есть пользовательские патчи, используем их (автоматические отбрасываем при конфликте)
// - Если нет пользовательских, используем автоматические
var selected = SelectBestPatchSet(userPatchSets, autoPatchSets);
return selected;

// [ЗН-10] SelectBestPatchSet с приоритетом пользовательских аннотаций:
RecoveryPatchSet SelectBestPatchSet(List<RecoveryPatchSet> userSets, List<RecoveryPatchSet> autoSets)
{
    if (userSets.Count > 0)
    {
        // Пользовательские аннотации имеют приоритет
        var bestUser = userSets.MinBy(s => s.TotalCost);

        // Отбрасываем автоматические патчи, которые конфликтуют с пользовательскими
        var conflictingKeys = bestUser.Patches.Select(p => p.Key).ToHashSet();
        var nonConflictingAuto = autoSets.Where(s =>
            !s.Patches.Any(p => conflictingKeys.Contains(p.Key)));

        // Если автоматические патчи дополняют пользовательские (не конфликтуют), комбинируем
        if (nonConflictingAuto.Any())
        {
            var bestAuto = nonConflictingAuto.MinBy(s => s.TotalCost);
            return new RecoveryPatchSet(
                [.. bestUser.Patches, .. bestAuto.Patches],
                bestUser.TotalCost + bestAuto.TotalCost);
        }

        return bestUser;
    }

    // Только автоматические
    return autoSets.MinBy(s => s.TotalCost);
}
```

**Longest-match интеграция:**

При longest-match, альтернативы с recovery-узлами штрафуются:

```csharp
// В ParseRule, при выборе лучшего результата:
if (postNewPos > maxPos)
{
    maxPos = postNewPos;
    bestResult = postfixResult;
}
else if (postNewPos == maxPos)
{
    // При равной длине — предпочитаем результат с меньшим cost
    var newCost = CountRecoveryNodes(postNode);
    var bestCost = CountRecoveryNodes(bestResult.Node);
    if (newCost < bestCost)
    {
        bestResult = postfixResult;
    }
}
```

Дополнительно: если есть альтернатива без recovery-узлов и альтернатива с recovery-узлами, но с большей длиной — предпочитаем чистую альтернативу (если разница в длине не превышает порога, например, 3 токена).

**Файлы:**

- `ExtensibleParser/Recovery/MemoPatch.cs`
- `ExtensibleParser/Recovery/CostCalculator.cs`
- Модификация `ExtensibleParser/Parser.cs` — `ParseRule`, выбор лучшего результата

**Тесты:** `Tests/ParserTests/Recovery/CostModelTests.cs`

- `Test_Cost_MissingToken` — cost = 1
- `Test_Cost_SkippedText` — cost = 1 за пропущенный символ
- `Test_Cost_Comparison` — кандидат с меньшим cost побеждает

---

### 2.2. Генерация кандидатов на восстановление

Recovery engine анализирует контекст ошибки и генерирует наборы патчей. Каждый набор — это стратегия (вставка или пропуск) с модификацией Partial для continuation.

```csharp
List<RecoveryPatchSet> GenerateMemoPatches(RecoveryContextSnapshot snapshot, string input)
{
    var patchSets = new List<RecoveryPatchSet>();

    // === Стратегия 1: Вставка ожидаемого токена ===
    foreach (var partial in snapshot.PartialsAt(snapshot.ErrorPos))
    {
        foreach (var terminal in partial.Context.Expected)
        {
            var terminalPatch = CreateInsertTokenPatch(terminal, snapshot.ErrorPos);
            var partialPatch = CreatePartialContinuationPatch(partial, terminal, snapshot.ErrorPos);
            patchSets.Add(new RecoveryPatchSet([terminalPatch, partialPatch]));
        }
    }

    // === Стратегия 2: Пропуск текста до терминатора ===
    var terminators = snapshot.Stack.AggregateFollowSets();
    var skipResult = SkipAhead(snapshot.ErrorPos, input, terminators);
    if (skipResult.Found)
    {
        var skipPatch = CreateSkipTextPatch(snapshot, skipResult, terminators);
        var partial = FindPartialAt(snapshot.Memo, snapshot.ErrorPos);
        if (partial != null)
        {
            var partialPatch = CreatePartialContinuationPatch(partial, skipResult, terminators);
            patchSets.Add(new RecoveryPatchSet([skipPatch, partialPatch]));
        }
        else
        {
            patchSets.Add(new RecoveryPatchSet([skipPatch]));
        }
    }

    // === Стратегия 3: Конец строки — достроение через FollowSet ===
    if (snapshot.ErrorPos == input.Length)
    {
        foreach (var context in snapshot.Stack.Reverse())
        {
            var followSet = _followCalculator.GetFollowSet(context.RuleName);
            foreach (var terminal in followSet)
            {
                var terminalPatch = CreateInsertTokenPatch(terminal, snapshot.ErrorPos);
                patchSets.Add(new RecoveryPatchSet([terminalPatch]));
            }
        }
    }

    // === Стратегия 4: User-provided (Фаза 4) ===
    if (snapshot.Stack.FindWithOptions() is { } contextWithOptions)
    {
        patchSets.AddRange(contextWithOptions.Options.GeneratePatchSets(snapshot, input));
    }

    // === Выбор лучшего набора ===
    return SelectBestPatchSet(patchSets);
}
```

**`RecoveryPatchSet`** — набор патчей, которые применяются вместе:

```csharp
public record RecoveryPatchSet(
    MemoPatch[] Patches,
    int TotalCost  // сумма cost всех патчей
);
```

**`SelectBestPatchSet`** — выбирает набор с минимальным `TotalCost`.

**`SelectBestPatchSet`** — выбирает непересекающийся набор патчей с минимальным общим cost. Два патча пересекаются, если они модифицируют один и тот же ключ memo или если их позиции перекрываются.

**Файлы:**

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — `GenerateMemoPatches`, `SelectBestPatchSet`

**Тесты:** `Tests/ParserTests/Recovery/CandidateGenerationTests.cs`

- `Test_Generate_MissingToken` — генерирует missing token патч
- `Test_Generate_SkipAhead` — генерирует skip-ahead патч
- `Test_Generate_Both` — генерирует оба патча
- `Test_Generate_None` — не генерирует патчей, когда recovery невозможен
- `Test_SelectBest_NoOverlap` — выбранные патчи не пересекаются
- `Test_SelectBest_MinCost` — выбран набор с минимальным cost

---

### 2.3. Rollback с вставкой из Follow-Set

Самая сложная автоматическая стратегия. Когда парсинг упал внутри правила, recovery engine пытается сгенерировать набор патчей: вставляет пустой терминал из follow-set родительского правила и модифицирует `Partial` для continuation.

**Алгоритм:**

```
RollbackWithInsertion(snapshot, pos, input):
    // 1. Найти partial tree из Result.Partial в memo
    partialTree = snapshot.PartialMemo.FirstOrDefault().Value.Node
    partialPos = partialTree.EndPos

    // 2. Найти родительский контекст из стека
    parentContext = snapshot.Stack.Skip(1).First()

    // 3. Вычислить follow-set родительского правила
    followSet = _followCalculator.GetFollowSet(parentContext.RuleName)

    // 4. Для каждого терминала из follow-set:
    foreach terminal in followSet:
        // Патч 1: вставляем пустой терминал
        terminalPatch = CreateInsertTokenPatch(terminal, partialPos)

        // Патч 2: модифицируем Partial родительского правила
        parentPartial = FindPartialAt(snapshot.Memo, partialPos, parentContext.RuleName)
        if parentPartial:
            modifiedPartial = ModifyPartialForContinuation(parentPartial, parentContext.Context, partialPos)
            partialPatch = CreatePartialContinuationPatch(parentPartial, modifiedPartial)
            yield new RecoveryPatchSet([terminalPatch, partialPatch], TotalCost: 1)

    // 5. Также попробовать skip-ahead от текущей позиции
    skipResult = SkipAhead(pos, input, followSet)
    if skipResult.Found:
        skipPatch = CreateSkipTextPatch(snapshot, skipResult, followSet)
        yield new RecoveryPatchSet([skipPatch], TotalCost: skipResult.TerminalPos - pos)
```

**Пример на C-подобном языке:**

```
int foo()
{
  int x;
// } пропущена

int bar()  // без recovery: разобьётся как "int bar" = объявление переменной
{
}
```

С rollback:

- Путь A: вставить `}` перед `int bar()` → memo-патч на позиции пропущенной `}` → cost 1 → следующий проход парсера входит в `Block`, находит патч, пропускает ошибку, продолжает как новый member
- Путь B: пропустить `int bar` до `()` → memo-патч на позиции `int` → cost 2
- Выбирается Путь A (cost 1 < cost 2)

**Файлы:**

- `ExtensibleParser/Recovery/Strategies/RollbackWithInsertionStrategy.cs`
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — интеграция

**Тесты:** `Tests/ParserTests/Recovery/RollbackTests.cs`

- `Test_Rollback_MissingBrace` — пропущенная `}`
- `Test_Rollback_MissingSemicolon` — пропущенная `;`
- `Test_Rollback_ChoosesLowerCost` — выбирается путь с меньшим cost
- `Test_Rollback_ContinuesParsing` — после recovery парсинг продолжается до конца
- `Test_Rollback_MemoPatchApplied` — патч виден в memo на следующей итерации

---
 
## Фаза 3: Представление пропущенного текста в дереве
 
**Цель:** Обеспечить, чтобы все пропущенные символы были представлены в выходном дереве через `TerminalNode` + внешняя хэш-таблица в `Parser`.
 
### 3.1. Пропущенный текст во внешней хэш-таблице
 
Пропущенный текст не хранится в самом `TerminalNode`. Вместо этого `Parser` хранит внешнюю хэш-таблицу `_skippedTextMap: TerminalNode -> (StartPos, Length)`. `TerminalNode` содержит только `StartPos`, `EndPos`, `ContentLength`. `EndPos` может выходить за `StartPos + ContentLength`, покрывая пропущенный текст.
 
**API для доступа к пропущенному тексту (через Parser):**
 
```csharp
public static class RecoveryTreeExtensions
{
    // Получить пропущенный текст из TerminalNode через Parser
    public static string? GetSkippedText(this TerminalNode node, Parser parser, string input)
    {
        return parser.GetSkippedText(node, input);
    }
 
    // Проверить, является ли узел recovery-узлом (есть ли skipped text)
    public static bool IsRecovery(this TerminalNode node, Parser parser)
        => parser._skippedTextMap.ContainsKey(node);
 
    // Получить все узлы с recovery в поддереве
    public static IEnumerable<ISyntaxNode> GetAllRecoveryNodes(this ISyntaxNode node, Parser parser);
}
```
 
**Файлы:**
 
- `ExtensibleParser/Parser.cs` — добавление `_skippedTextMap`, `GetSkippedText`, `RegisterSkippedText`
- `ExtensibleParser/Recovery/RecoveryTreeExtensions.cs` — extension методы с параметром `Parser`
 
**Тесты:** `Tests/ParserTests/Recovery/RecoveryTreeTests.cs`
 
- `Test_SkippedText_InExternalMap` — пропущенный текст во внешней хэш-таблице
- `Test_RecoveryNodes_Found` — все recovery-узлы найдены через Parser
- `Test_NothingLost` — каждый символ входного текста представлен в дереве
 
---

### 3.2. Diagnostics collection

Собранные `RecoveryDiagnostic` должны быть доступны после парсинга:

```csharp
public class Parser
{
    // Существующее:
    public FatalError? ErrorInfo { get; }

    // Новое:
    public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics { get; }
}
```

**Файлы:**

- `ExtensibleParser/Parser.cs` — добавление свойства

**Тесты:** `Tests/ParserTests/Recovery/DiagnosticsTests.cs`

- `Test_Diagnostics_Collected` — диагностики собраны
- `Test_Diagnostics_Positions` — позиции корректны
- `Test_Diagnostics_Kinds` — виды ошибок корректны

---

## Фаза 4: Пользовательские расширения через аннотации грамматики

**Цель:** Позволить авторам языков добавлять кастомные стратегии восстановления через декларативные аннотации.

### 4.1. RecoveryRule — обёртка правила с аннотациями восстановления

```csharp
public record RecoveryRule(
    Rule Inner,
    RecoveryOptions Options = default
) : Rule(Inner.Kind)
{
    // Inherit все поведение Inner, но с recovery-опциями
}

public record RecoveryOptions(
    // Терминаторы: какие терминалы могут завершать это правило
    Terminal[]? Terminators = null,

    // Предикат начала: правило, которое определяет, может ли текущая позиция
    // начать новую итерацию цикла (например, "может начать стейтмент")
    Rule? CanStartNext = null,

    // При ошибке: какие токены попробовать вставить
    Terminal[]? TryInsert = null,

    // При ошибке: до каких токенов пропускать
    Terminal[]? SkipTo = null,

    // Максимальная глубина вложенности для recovery
    int? MaxRecoveryDepth = null,

    // Пользовательский предикат остановки (как TerminatorState в Roslyn)
    Func<string, int, bool>? StopPredicate = null
)
```

**Пример использования (MiniC):**

```csharp
_parser.Rules["Statement"] = new Rule[]
{
    new RecoveryRule(
        new Seq([new Literal("int"), Terminals.Ident(), new Literal(";")], "VarDecl"),
        new RecoveryOptions(
            Terminators = [new Literal("}"), new Literal("else")],
            TryInsert = [new Literal(";")],
            SkipTo = [new Literal("}")]
        )
    ),
    // ...
};

_parser.Rules["Block"] = new Rule[]
{
    new RecoveryRule(
        new Seq([
            new Literal("{"),
            new ZeroOrMany(
                new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")
            ),
            new Literal("}")
        ], "MultiBlock"),
        new RecoveryOptions(
            Terminators = [new Literal("}")],
            CanStartNext = new Alt([new Literal("int"), Terminals.Ident(), new Literal("if"), new Literal("return")]),
            SkipTo = [new Literal("}")]
        )
    )
};
```

**Интеграция с RecoveryEngine:**

```csharp
// В GenerateRecoveryCandidates:
var context = _recoveryStack.FindWithAction();
if (context?.Action is { } options)
{
    // 1. TryInsert
    foreach (var terminal in options.TryInsert ?? [])
    {
        candidates.Add(CreateMissingTokenCandidate(terminal, pos));
    }

    // 2. SkipTo
    foreach (var terminal in options.SkipTo ?? [])
    {
        candidates.Add(CreateSkipToCandidate(terminal, pos, input));
    }

    // 3. CanStartNext — используется для определения, когда остановить skip-ahead
    if (options.CanStartNext != null)
    {
        terminators.Add(options.CanStartNext);
    }

    // 4. StopPredicate — пользовательский предикат остановки
    if (options.StopPredicate != null)
    {
        customStopCheck = options.StopPredicate;
    }
}
```

**Файлы:**

- `ExtensibleParser/Rules.cs` — `RecoveryRule`, `RecoveryOptions`
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — интеграция
- Модификация `ExtensibleParser/Parser.cs` — обработка `RecoveryRule`

**Тесты:** `Tests/ParserTests/Recovery/RecoveryRuleTests.cs`

- `Test_TryInsert_Semicolon` — вставка `;`
- `Test_SkipTo_ClosingBrace` — пропуск до `}`
- `Test_CanStartNext_Statement` — определение начала стейтмента
- `Test_StopPredicate_Custom` — пользовательский предикат остановки
- `Test_RecoveryRule_WithMiniC` — интеграция с MiniC грамматикой

---

### 4.2. TerminatorPredicate — предикат терминатора

Аналог `TerminatorState` из Roslyn, но в виде правила грамматики:

```csharp
public record TerminatorPredicate(
    Rule Predicate,
    string Name
) : Rule($"Terminator:{Name}")
{
    // Матчится если Predicate матчится на текущей позиции
    // Используется как условие остановки для skip-ahead
}
```

**Пример:**

```csharp
// "Может начать член типа"
var canStartMember = new TerminatorPredicate(
    new Alt([
        new Literal("public"),
        new Literal("private"),
        new Literal("protected"),
        new Literal("static"),
        new Literal("int"),
        new Literal("void"),
        Terminals.Ident(),
        new Literal("~"),
        new Literal("["),
    ]),
    "CanStartMember"
);

_parser.Rules["MemberList"] = new Rule[]
{
    new RecoveryRule(
        new ZeroOrMany(new Ref("Member")),
        new RecoveryOptions(
            Terminators = [new Literal("}")],
            CanStartNext = canStartMember.Inner,
            SkipTo = [new Literal("}")]
        )
    )
};
```

**Файлы:**

- `ExtensibleParser/Rules.cs` — `TerminatorPredicate`

**Тесты:** `Tests/ParserTests/Recovery/TerminatorPredicateTests.cs`

- `Test_TerminatorPredicate_Matches` — предикат матчится
- `Test_TerminatorPredicate_UsedInSkip` — используется в skip-ahead

---

### 4.3. Integration с существующими механизмами

**OftenMissed → RecoveryRule:**

`OftenMissed` становится частным случаем `RecoveryRule`:

```csharp
// Вместо:
new OftenMissed(new Literal("}"))

// Можно писать:
new RecoveryRule(
    new Literal("}"),
    new RecoveryOptions(TryInsert = [new Literal("}")])
)
```

Существующий `OftenMissed` сохраняется для обратной совместимости, но внутренне делегирует в `RecoveryRule`.

**RecoveryTerminal → Trivia absorption:**

`RecoveryTerminal` поглощается в тривиал предыдущего `TerminalNode`. Если терминал матчится на неожиданной позиции, его текст записывается в разницу между `EndPos` и `StartPos + ContentLength` предыдущего узла. `IsRecovery` определяется через наличие записи в `_skippedTextMap`.

---

## Фаза 5: Финальная интеграция и полировка

### 5.1. Longest-match с recovery-aware scoring

Финальная интеграция recovery в longest-match логику:

```csharp
// В ParseRule, при сравнении альтернатив:
bool IsBetterResult(Result newResult, Result? bestResult, int newPos, int maxPos)
{
    if (newPos > maxPos)
        return true;  // Длиннее — всегда лучше

    if (newPos < maxPos)
        return false; // Коротче — хуже

    // Равная длина: сравниваем по cost
    if (bestResult is null)
        return true;

    var newCost = _costCalculator.Calculate(newResult.Node);
    var bestCost = _costCalculator.Calculate(bestResult.Value.Node);

    if (newCost < bestCost)
        return true;  // Меньше ошибок — лучше

    if (newCost > bestCost)
        return false;

    // Равный cost: предпочитаем больше покрытия (меньше skipped tokens)
    var newSkipped = CountSkippedTrivia(newResult.Node);
    var bestSkipped = CountSkippedTrivia(bestResult.Value.Node);
    return newSkipped < bestSkipped;
}
```

**Threshold для "clean vs dirty" альтернатив:**

Если разница в длине между чистой альтернативой (без recovery) и альтернативой с recovery превышает порог (по умолчанию 3 токена), предпочитаем более длинную даже с recovery. Это предотвращает ситуацию, когда короткий чистый матч побеждает длинный recovery-матч, который покрывает больше кода.

```csharp
const int CleanPreferenceThreshold = 3; // токена

if (newCost == 0 && bestCost > 0)
{
    // Новая альтернатива чистая, текущая best — с recovery
    var lengthDiff = maxPos - newPos; // разница в символах
    var tokenDiff = EstimateTokenCount(lengthDiff);
    if (tokenDiff <= CleanPreferenceThreshold)
        return false; // Предпочитаем чистый, разница небольшая
}
```

**Тесты:** `Tests/ParserTests/Recovery/LongestMatchRecoveryTests.cs`

- `Test_CleanBeatsDirty_SameLength` — чистый матч побеждает при равной длине
- `Test_DirtyBeatsClean_Longer` — recovery-матч побеждает, если значительно длиннее
- `Test_CostBreaksTie` — cost разбивает ничью
- `Test_Threshold_CleanPreference` — порог предпочтения чистого матча

---

### 5.2. End-to-end тесты

Комплексные тесты на MiniC-подобном языке:

**Тесты:** `Tests/ParserTests/Recovery/EndToEndTests.cs`

```csharp
[TestMethod]
public void Test_MissingClosingBrace_Function()
{
    // int foo() { int x; x = 5;
    // int bar() { return 0; }
    // Ожидаем: foo без }, bar корректно разобран
}

[TestMethod]
public void Test_MissingSemicolon_MultipleStatements()
{
    // int x; int y int z;
    // Ожидаем: y с missing ;
}

[TestMethod]
public void Test_UnexpectedToken_Expression()
{
    // x = 1 @ 2;
    // Ожидаем: @ как excess token, парсинг продолжается
}

[TestMethod]
public void Test_NestedErrors_MultipleBlocks()
{
    // { int x; { int y; int z { return 0; }
    // Ожидаем: multiple missing }, recovery до конца
}

[TestMethod]
public void Test_Recovery_DoesNotBreakCorrectCode()
{
    // Корректный код не должен содержать recovery-узлов
}

[TestMethod]
public void Test_EverythingRepresentedInTree()
{
    // Каждый символ входного текста представлен в дереве
}

[TestMethod]
public void Test_ParsingReachesEndOfString()
{
    // Парсинг всегда доходит до конца строки
}
```

---

### 5.3. API обобщения

Финальный публичный API парсера с recovery:

```csharp
public class Parser(Terminal trivia, Log? log = null)
{
    // Существующее:
    public Dictionary<string, Rule[]> Rules { get; }
    public void BuildTdoppRules();
    public Result Parse(string input, string startRule, out int triviaLength);

    // Новое:
    /// Собранные диагностики восстановления
    public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics { get; }

    /// Стек восстановления (для отладки и расширений) — реконструируется из memo
    public IReadOnlyList<ParseContext>? LastRecoveryStack { get; }

    /// Включить/выключить recovery (по умолчанию true)
    public bool RecoveryEnabled { get; set; } = true;

    /// Порог предпочтения чистого матча над recovery-матчем (в токенах)
    public int CleanPreferenceThreshold { get; set; } = 3;

    /// Максимальное количество recovery-итераций на одну позицию
    public int MaxRecoveryAttemptsPerPosition { get; set; } = 5;
}
```

---

## Сводная таблица фаз

| **0.3** | Представление ошибок в дереве          | `TerminalNode.cs`, `RecoveryDiagnostic.cs`   | `ErrorRepresentationTests.cs`  |
| **1.1** | Memo-инъекция (инкрементальный подход)   | `RecoveryEngine.cs`, `Parser.cs`             | `IterativeRecoveryTests.cs`    |
| **1.2** | Вставка ожидаемого токена                | `InsertTokenStrategy.cs`                     | `InsertTokenTests.cs`          |
| **1.3** | Skip-Ahead до Follow-Set                 | `SkipAheadStrategy.cs`                       | `SkipAheadTests.cs`            |
| **1.4** | OftenMissed улучшение                    | `Parser.cs`                                  | `OftenMissedTests.cs`          |
| **2.1** | Cost Model                               | `RecoveryCandidate.cs`, `CostCalculator.cs`  | `CostModelTests.cs`            |
| **2.2** | Candidate Generation                     | `RecoveryEngine.cs`                          | `CandidateGenerationTests.cs`  |
| **2.3** | Rollback с вставкой                      | `RollbackWithInsertionStrategy.cs`           | `RollbackTests.cs`             |
| **3.1** | Пропущенный текст во внешнем мапе         | `Parser.cs`, `RecoveryTreeExtensions.cs`     | `RecoveryTreeTests.cs`         |
| **3.2** | Diagnostics collection                   | `Parser.cs`                                  | `DiagnosticsTests.cs`          |
| **4.1** | RecoveryRule                             | `Rules.cs`, `RecoveryEngine.cs`              | `RecoveryRuleTests.cs`         |
| **4.2** | TerminatorPredicate                      | `Rules.cs`                                   | `TerminatorPredicateTests.cs`  |
| **4.3** | Интеграция с существующим                | `Rules.cs`, `Parser.cs`                      | —                              |
| **5.1** | Longest-match scoring                    | `Parser.cs`, `CostCalculator.cs`             | `LongestMatchRecoveryTests.cs` |
| **5.2** | End-to-end тесты                         | —                                            | `EndToEndTests.cs`             |
| **5.3** | API обобщения                            | `Parser.cs`                                  | —                              |

---

## Риски и митигация

| Риск                                                  | Влияние                                                                        | Митигация                                                                                                                           |
| ----------------------------------------------------- | ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| **Итеративный цикл медленный**                        | O(ошибки × ввод) — каждый проход парсит от начала                              | Memo кэширует успешный префикс; memo-патчи ускоряют прохождение ошибок; на практике 2-4 прохода на файл                             |
| **Regex-матчинг в skip-ahead дорог**                  | O(терминалы × позиции) на каждую ошибку                                        | Кэшировать TryMatch результаты; ограничить максимальное количество проверок на позицию                                              |
| **Follow-set для regex-терминалов неточен**           | First-set терминала = сам терминал, но regex может матчить множество паттернов | Follow-set работает на уровне `Kind` терминала, а не regex. Точность определяется автором языка через `RecoveryOptions.Terminators` |
| **Recovery зацикливается**                            | Бесконечный цикл при определённых грамматиках                                  | `MaxRecoveryAttemptsPerPosition`; проверка прогресса (позиция должна двигаться вперёд)                                              |
| **Longest-match с recovery даёт странные результаты** | Recovery-путь длиннее чистого, но семантически неверен                         | `CleanPreferenceThreshold` — порог, после которого длинный recovery-путь побеждает                                                  |
| **Partial memo не содержит достаточно информации**    | Rollback не может восстановить контекст                                        | Контекст хранится в Result.Partial; стек восстанавливается из memo                                                                   |
| **Итеративный recovery ломает memo**                  | Кэшированный failure на позиции X мешает recovery в следующем проходе          | Очистка memo на `ErrorPos` и `currentStartPos` уже реализована; memo-патчи перезаписывают failure-записи success-результатами       |
| **Назад несовместимость с существующим кодом**        | Существующие грамматики ломаются                                               | Все новые механизмы опциональны; `RecoveryEnabled = false` отключает recovery                                                       |

---

## Порядок работы

1. **Фаза 0** — инфраструктура. Без неё невозможно тестировать остальные фазы.
2. **Фаза 1** — базовое восстановление. Уже на этом этапе парсер сможет восстанавливаться от пропущенных токенов и неожиданных символов через итеративный цикл с memo-инъекцией.
3. **Фаза 2** — multi-path. Улучшает качество восстановления, выбирая лучший путь по cost.
4. **Фаза 3** — дерево. Обеспечивает, что ничего не теряется.
5. **Фаза 4** — пользовательские расширения. Позволяет авторам языков тонко настраивать восстановление через аннотации грамматики.
6. **Фаза 5** — полировка. Интеграция всех механизмов, longest-match scoring с recovery-aware cost, end-to-end тестирование.

Каждая фаза может быть коммитаема и тестируема независимо. Фаза N зависит только от фазы N-1 (и Фазы 0).

### Итеративный цикл — как это работает на практике

Пример: строка с 3 ошибками.

| Проход | ErrorPos | Что происходит                                                                                                                                 | Result                    |
| ------ | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------- |
| 1      | 42       | Парсинг от начала, все пути падают на позиции 42. `ErrorPos = 42`.                                                                             | Failure                   |
| 2      | 42       | Recovery engine генерирует memo-патч (пустой терминал). Префикс [0..42) из memo. Парсер входит в те же правила, находит патч, падает на 127.   | Failure, `ErrorPos = 127` |
| 3      | 127      | Recovery engine генерирует memo-патч (пропуск во внешний мап). Префикс [0..127) из memo. Парсер продолжается, падает на позиции 203.                | Failure, `ErrorPos = 203` |
| 4      | 203      | Recovery engine генерирует memo-патч. Префикс [0..203) из memo. Парсер доходит до конца строки.                                                | **Success**               |

Каждый проход парсит от начала, но благодаря memo фактическая работа — только от предыдущей `ErrorPos` до новой. Memo-патчи позволяют парсеру пройти через ошибку в следующем проходе, не дожидаясь возврата на верхний уровень.

---

## Приложения: Интеграция замечаний от LLM-ревью

### ЗН-1: Memo-инъекция терминалов

**Проблема:** `ParseTerminal` не обращается к `_memo`. Патчи вида `(pos, terminal.Kind, 0)` не будут видны парсеру.

**Решение:** Отдельный кэш `_terminalMemo` для результатов `TryMatch`. `ParseTerminal` проверяет этот кэш перед вызовом `terminal.TryMatch()`.

**Реализация:** Фаза 1.1, `_terminalMemo`, модификация `ParseTerminal`.

---

### ЗН-2: Partial memo привязан к `_recoverySkipPos`

**Проблема:** `ParseRule` проверяет `_partialMemo` только при `startPos == _recoverySkipPos`. После итерации `_recoverySkipPos` сменится, и recovery-патч будет проигнорирован.

**Решение:** Флаг `IsRecoveryPatch` в `ParseContext`. Проверка `_partialMemo` всегда, с условием `IsRecoveryPatch == true`.

**Реализация:** Фаза 1.1, модификация `ParseRule`.

---

### ЗН-3: Инвариант прогресса при пустом токене

**Проблема:** Вставка пустого `TerminalNode` не двигает позицию. Может привести к зацикливанию.

**Решение:** Проверка, что модификация Partial гарантированно сдвигает позицию хотя бы на 1 символ. `MaxRecoveryAttemptsPerPosition` как защитный механизм.

**Реализация:** Фаза 1.1, `RecoveryEngine.GeneratePatchSets`.

---

### ЗН-4: Смешение trivia и пропущенного текста

**Проблема:** Хранение пропущенного текста в разнице `EndPos - (StartPos + ContentLength)` смешивает реальный trivia и recovery-мусор.

**Решение:** Внешняя хэш-таблица `_skippedTextMap: TerminalNode -> (pos, len)` в `Parser`. `TerminalNode` без изменений (нет `SkippedText`, нет `IsRecovery`). `IsRecovery` — extension-метод через `parser._skippedTextMap.ContainsKey(node)`. API `GetSkippedText(node, parser, input)`.

**Реализация:** Фаза 0.3, модификация `TerminalNode` (без изменений), добавление `_skippedTextMap` в `Parser`.

---

### ЗН-5: Производительность при переходе от struct к классам

**Проблема:** Замена `readonly record struct Result` на иерархию классов увеличит аллокации.

**Решение:** Сохранить `readonly record struct`. Добавить nullable `ParseContext?` только для Partial-результатов.

**Реализация:** Фаза 0.2, модификация `Result`.

---

### ЗН-6: Судьба существующего recovery-кода

**Проблема:** Методы `ContinueFromPartial`, `TryRecoverFromPartial`, `MergeWithPartialTree` и `RecoveryPrefix`/`RecoveryPostfix` конфликтуют с новой архитектурой.

**Решение:** Явный план миграции:
- Удаляется: `ContinueFromPartial`, `TryRecoverFromPartial`, `MergeWithPartialTree`, `RecoveryPrefix`/`RecoveryPostfix`, `_recoverySkipPos`
- Остаётся: `OftenMissed` (частный случай), `RecoveryTerminal`, `EmptyTerminal`
- Упрощается: `BuildTdoppRules` (убирает recovery-ветки)

**Реализация:** Фаза 0.4 → Фаза 1.1 → Фаза 2 → Фаза 3.

---

### ЗН-7: Политика очистки memo

**Проблема:** Для стабильности multi-path нужен чёткий алгоритм `CleanMemoForRecovery`.

**Решение:** Удаляем: Failure с `MaxFailPos == ErrorPos`, Failure на `currentStartPos`, Success с `NewPos == ErrorPos`, Partial с `NewPos == ErrorPos`. Очищаем `_terminalMemo` на `ErrorPos`.

**Реализация:** Фаза 1.1, `CleanMemoForRecovery`.

---

### ЗН-8: FollowSet на regex-терминалах

**Проблема:** FollowSet оперирует инстансами терминалов. `TryMatch` на каждой позиции дорог.

**Решение:** Кэш `TryMatch` результатов в `SkipAheadStrategy`. Автор языка может уточнить терминаторы через `RecoveryOptions.Terminators`.

**Реализация:** Фаза 1.3, `CachedTryMatch`.

---

### ЗН-9: Стратегия «замена токена»

**Проблема:** Cost-model включает замену (cost=2), но не описана генерация такого патча.

**Решение:** Замена = комбинация skip + insert. Cost = 2 (1 за skip + 1 за insert).

**Реализация:** Фаза 2.1, cost model.

---

### ЗН-10: Приоритет пользовательских аннотаций

**Проблема:** Если автоматическая стратегия и `RecoveryOptions` предлагают одинаковые терминалы, не ясен приоритет.

**Решение:** Пользовательские аннотации имеют приоритет. Автоматические не применяются при конфликте (по ключу memo). Не конфликтующие автоматические патчи комбинируются с пользовательскими.

**Реализация:** Фаза 2.1, `SelectBestPatchSet`.

---

### ЗН-11: Проблема предикатов

**Проблема:** Спекулятивный разбор внутри `AndPredicate`/`NotPredicate` может изменить глобальные `ErrorPos` и `_expected`, искажая позицию ошибки.

**Решение:** Сохранение и восстановление `ErrorPos` и `_expected` до/после спекулятивного разбора в предикатах.

**Реализация:** Фаза 1.1, модификация `ParseAndPredicate`, `ParseNotPredicate`.
