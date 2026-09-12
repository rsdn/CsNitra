# Recovery System v2 — Implementation Checklist

**План:** `docs/RecoverySystemPlan.v2.md` (v1 — в `docs/Old/`, superseded).
**Тестирование:** на MiniC (грамматика `Tests/ParserTests/MiniC/`).
**Процесс:** каждый пункт — отдельный коммит. Субагент реализует один пункт, доводит до компилируемости, пишет тест к пункту, отлаживает, и дописывает в «Заметки» что сделал, с какими проблемами столкнулся и какие решения принял. Основной агент проверяет качество, помечает пункт `[x]` и коммитит.

**Легенда статуса:**
- `[ ]` — не начат
- `[~]` — в работе (субагент запущен)
- `[x]` — выполнен и проверен
- `[!]` — есть проблемы / отложено

**Текущий пункт:** 1.3.2

---

## Фаза 0. Фундамент парсера (исправления кода, нового recovery-поведения нет)

Цель: починить базу, на которой строится engine. После Фазы 0 recovery-поведение на корректных и текущих некорректных входах не меняется (кроме устранения зацикливания), но `Partial` и снимки становятся реальными.

### 0.1 Дисциплина `_ruleStack` → `List<StackFrame>`
- [x] Статус — выполнен, проверен (сборка 0 ошибок; StackFrameTests 4/4; ParserTests 176/176)
- **Что:** push с локациями (Seq/Loop/Postfix/Alt + `Expected` + `Precedence` + `Options`), pop в `finally`.
- **Файлы:** `Parser.cs`, `Recovery/StackFrame.cs`
- **Тесты:** `StackFrameTests`: глубина/локации/очистка при исключениях.
- **Заметки:**
  - Создан `Recovery/StackFrame.cs` (`StackFrame` + `FrameLocation` и 4 локации). `RuleStackEntry`/`_ruleStack` заменены на `List<StackFrame> _stackFrames`; доступ для тестов — `public IReadOnlyList<StackFrame> Parser.CurrentStackFrames` (в проекте нет InternalsVisibleTo, поэтому свойство public).
  - Push-дисциплина: `ParseRule` — `RuleFrameLocation(altIdx)` перед каждой альтернативой (prefix), `ParseSeq` — `SeqFrameLocation(i)` + `Expected`, `ParseOneOrMany`/`ParseZeroOrMany` — `LoopFrameLocation(kind, iteration)` (первый элемент OneOrMany = iteration 0, второй элемент любого цикла = iteration 1), `TryParsePostfix` — `PostfixFrameLocation(i)`. Все pop'ы — в `finally` (хелперы `WithFrame`/`PushFrame`/`PopFrame`); устранён пропуск pop на раннем возврате через `_partialAccumulated`.
  - Ненулевые (не-правило) кадры наследуют `RuleName`/`Precedence` от кадра сверху (fallback — kind правила при пустом стеке, на практике недостижимо).
  - `Expected` — временный локальный расчёт `ComputeExpected` (Terminal → `[t]`; Seq → first первого элемента рекурсивно; `OneOrMany` → first элемента; `SeparatedList` → `[]` если `CanBeEmpty`, иначе first элемента; `Optional`/`ZeroOrMany`/`OftenMissed`/предикаты → `[]`; `Ref`/`ReqRef` → `null`). Заменится на `FirstSets.Get` в пункте 0.3.
  - `Options` всегда `null` (`RecoveryRule` появится в 1.5/2.1). Логика v1 (`_partialMemo`, `ContinueFromPartial`, `_partialAccumulated`, RecoveryPrefix/Postfix) не тронута.
  - Лог "RULE STACK TRACE" в `Parse` переписан под новую структуру (Rule/Prec/Loc/Expected); в момент вывода стек пуст (все pop'ы отработали) — живой снимок в момент ошибки появится в пункте 0.2.
  - **Проблема (вне пункта):** HEAD-коммит 9497b0d содержит ошибку сборки `CppInteropGenerator/EnumGenerator.cs:213` — `IndexOf(char, StringComparison)` недоступен в netstandard2.0 (коммит «fix netstandard2.0 build error» заменил один недоступный API на другой). Решение: минимальное однострочное исправление `IndexOf('_')` — без него `dotnet build Nitra.sln` не даёт 0 ошибок и не на чём проверять регрессию.
  - **Результаты:** сборка `Nitra.sln` — 0 ошибок (57 предупреждений — все предсуществующие); `StackFrameTests` — 4/4 зелёные (глубина/локации на MiniC-подобной грамматике Module→ZeroOrMany(Function)→Seq, очистка после успеха, очистка при исключении из `TryMatch`, Iteration==1 для второго элемента обоих циклов); регрессия `Tests/ParserTests` — 176 пройдено, 0 упало, 2 пропущено (предсуществующие `[Ignore("WIP")]`).

### 0.2 `FailureSnapshot`
- [x] Статус — выполнен, проверен (SnapshotTests 6/6; ParserTests 182/182) — в работе (сборка 0 ошибок; SnapshotTests 6/6; ParserTests 182 passed / 0 failed / 2 skipped)
- **Что:** `CaptureSnapshot` в точке самого дальнего mismatch, `_lastSnapshot`, `Speculative`-хелпер, изоляция предикатов + speculative-проверки `SeparatedList`.
- **Файлы:** `Parser.cs`, `Recovery/FailureSnapshot.cs`
- **Тесты:** `SnapshotTests`: снимок при самом дальнем mismatch; спекуляция не портит снимок/`ErrorPos`/`_expected`.
- **Заметки:**
  - Создан `Recovery/FailureSnapshot.cs` (`sealed record FailureSnapshot(Pos, Stack, FailedTerminal, Expected)`). В `Parser.cs` добавлены: поле `_lastSnapshot` + `public FailureSnapshot? LastSnapshot`, флаг `_suppressSideEffects`, метод `CaptureSnapshot(pos, failedTerminal)` (копия `_stackFrames` через `ToArray`; `Expected` = `Expected` верхнего кадра, если не null, + `failedTerminal`, дубли отброшены через `List.Contains`), хелпер `Speculative<T>(Func<T>)`.
  - `ParseTerminal`: блок «самой дальней точки падения» теперь проверяет `!_suppressSideEffects`, а в ветке `startPos > ErrorPos` (обновление дальнего mismatch) вызывает `CaptureSnapshot(startPos, terminal)`. Возврат `Result.Failure` не зависит от флага — изоляция трогает только побочные эффекты, не результат парсинга.
  - В `Speculative` обёрнуты: внутренний парсинг в `ParseAndPredicate` и `ParseNotPredicate` (вручную сделанный save/restore `ErrorPos`/`_expected` заменён на хелпер), финальная спекулятивная проверка trailing separator (`SeparatorEndBehavior.Forbidden`) в `ParseSeparatedList` (проверка `ParseAlternative(Separator)` без «съедания»). В `Parse` при инициализации: `_lastSnapshot = null`, `_suppressSideEffects = false`.
  - **Решение (comparator):** `Speculative` восстанавливает `_expected = new HashSet<Terminal>(savedExpected)` — дефолтный компаратор, как у текущего `[]`-инициализатора. `TerminalComparer.Instance` из плана §3.3.4 — это Фаза 0.4 (типа ещё нет), поэтому не используется.
  - **Решение (readonly):** `_expected` раз-`readonly` (было `private readonly HashSet<Terminal> _expected = []`) — хелпер переназначает поле. Совпадает с планом (в 0.4 там будет `new HashSet<Terminal>(…, TerminalComparer.Instance)`).
  - **Проблема (вне пункта, фикс):** в csproj от 0.1 остались явные `<Compile Include="Recovery\StackFrame.cs" />` (ExtensibleParser) и `<Compile Include="Recovery\StackFrameTests.cs" />` (ParserTests) — они дублируют default-глоб SDK и давали `NETSDK1022` (ошибка сборки всего решения). Оба явных include удалены — файлы подхватываются глобом. Без этого `dotnet build Nitra.sln` не даёт 0 ошибок.
  - **Проблема (тест 3/4, AndPredicate):** пример из ТЗ (`Seq([a, &bc, x])`, вход `"a b Q"`) не воспроизводится: падение внутреннего парсинга `&`-предиката ⇒ сам предикат падает ⇒ `Seq` останавливается до `x`, т.е. `x` никогда не парсится и `ErrorPos` остаётся 0. Заменил на Alt из двух веток: ветка 1 `Seq([a, &bc, y])` (предикат спекулятивно падает на дальнем «Q»=4), ветка 2 `Seq([a, z])` (реальный mismatch `z` на 2). С изоляцией `ErrorPos`/снимок = 2 (`z`), без — был бы 4. Терминалы тела предиката (`b`,`c`) не попадают в `_expected`. Для `NotPredicate` пример из ТЗ работает как есть (падение внутреннего парсинга ⇒ `!P` успешен ⇒ Seq продолжается ⇒ реальный mismatch на `x`), вход `"a b Q"` сохранён.
  - **Компромисс (флаг при вложенности):** `finally` в `Speculative` ставит `_suppressSideEffects = false` (как в плане §3.3.4). При вложенной спекуляции внутренний `finally` сбросит флаг раньше, но внешний `finally` всё равно восстанавливает `ErrorPos`/`_expected`/`_lastSnapshot`, поэтому итоговая изоляция корректна (вложенных предикатов в текущих грамматиках нет).
  - **Среда:** RoslynMcpServer удерживал `TerminalGenerator.dll` (централизованный `bin/`), сборка из консоли не могла перезаписать dll — MCP-сервер остановлен перед сборкой.
  - **Результаты:** `dotnet build Nitra.sln` — 0 ошибок (57 предупреждений — все предсуществующие); `SnapshotTests` — 6/6 зелёные; регрессия `Tests/ParserTests` — 182 passed / 0 failed / 2 skipped (2 — предсуществующие `[Ignore("WIP")]`; 182 = 176 старых + 6 новых).

### 0.3 Базовый случай `Partial` в `ParseSeq`
- [x] Статус — выполнен, проверен (ParserTests 207/207). Часть А: FirstSets + миграция ParseContext. Часть Б: базовый случай Partial + тай-брейк + guard нуля; хак 336-343 оставлен, удаление переносится в 1.5.
- **Что:** базовый случай Partial + `FirstSets.Get` + тай-брейк (Success бьёт Partial при равной длине) + guard нуля в `OneOrMany`/`ZeroOrMany` (break при `newPos == currentPos`) + удаление хака `Parser.cs:336-343`.
- **Файлы:** `Parser.cs`, `Recovery/FirstSets.cs`
- **Тесты:** `PartialBaseCaseTests`: `int x int y;` даёт Partial с `SeqFrameLocation(2)`; ε-цикл не виснет; тай-брейки.
- **Заметки:**
  - **Часть А (FirstSets + ParseContext миграция) — выполнено:**
    - Создан `Recovery/FirstSets.cs`: `FirstSets.Get(Rule, FollowSetCalculator?)` + `FirstSets.IsNullable(Rule, FollowSetCalculator?)`. Семантика §3.6: `Terminal` (вкл. Literal/RecoveryTerminal/EmptyTerminal) → `[t]`, не nullable; `Seq` → first-цепочка с nullable-сходом; `OneOrMany` → first(el), не nullable; `ZeroOrMany`/`Optional`/`OftenMissed` → first(el), nullable; `AndPredicate`/`NotPredicate` → `[]`, nullable; `Ref`/`ReqRef` → `calculator?.GetFirstSet(name) ?? []`; `SeparatedList` → `CanBeEmpty ? [] : first(el)`, nullable = CanBeEmpty; `TdoppRule` → объединение first по `Prefix`, nullable если какой-то prefix nullable; незнакомый тип → `[]`, не nullable. Дедупликация по value-equality (Literal по Value, остальное по инстансу): `FollowSetCalculator.TerminalEqualityComparer` приватный → логика продублирована в FirstSets (просто, без изменения видимости).
    - **Решение (ε):** `FollowSetCalculator.GetFirstSet` возвращает ε-терминал (Kind `"ε"`) для nullable-правил; в FirstSets для `Ref` он отфильтрован (`.Where(t => t.Kind != "ε")`) — контракт «ε не представляется».
    - **Решение (IsNullable для Ref):** `FollowSetCalculator.IsNullable(string)` сделан `public` (был `private`) — используется в FirstSets для `Ref`/`ReqRef`; без calculator → false.
    - `ParseContext.Location`: `ParseLocation` → `FrameLocation`; удалены типы `ParseLocation`/`SeqLocation`/`LoopLocation`/`PrefixLocation` (`RecoveryOptions` не тронут). В `Parser.cs` при создании `ParseContext`: `new PrefixLocation(i)`→`new RuleFrameLocation(i)`, `new SeqLocation(i)`→`new SeqFrameLocation(i)`, `new LoopLocation(k,i)`→`new LoopFrameLocation(k,i)`. `RecoveryStackReconstructor`: `SeqLocation(0)`→`SeqFrameLocation(0)`, `Location.GetDepth()` (всегда 0, метода в `FrameLocation` нет)→константа `0`.
    - Замена: все `ComputeExpected(...)` и `GetExpectedTerminals(...)` → `FirstSets.Get(..., _followCalculator)`; оба старых метода удалены. Калькулятор инициализируется жадно в конструкторе Parser (на практике не null); null-ветка в FirstSets — защитная (`Ref` → `[]`).
    - **Следствие (metadata):** `Expected` в кадре Seq-элемента-`Ref` теперь = First(правила) (а не null, как у старого `ComputeExpected`). Обновлён `StackFrameTests.Test_Stack_Depth_And_Locations` (элемент `Ref("Block")`: `Expected` = `{"{","["}`). Поведение парсинга (дерево/позиции/результат) НЕ изменилось.
    - **Тесты:** новый `FirstSetsTests` (20 тестов: Terminal/EmptyTerminal, Seq-цепочки (вкл. глубокую nullable и dedup), OneOrMany/ZeroOrMany/Optional/OftenMissed, предикаты, SeparatedList (±CanBeEmpty), Ref с/без calculator, ε-фильтр, TdoppRule, IsNullable-варианты). `ParseContextTests` обновлён на FrameLocation-аналоги (`Test_ParseLocation_Types`→`Test_FrameLocation_Types`, `PrefixIndex`→`AltIndex`).
    - **Результаты:** сборка `Nitra.sln` — 0 ошибок; `FirstSetsTests` — 20/20; регрессия `Tests/ParserTests` — **202 passed / 0 failed / 2 skipped** (182 базовых + 20 новых; 2 — предсуществующие `[Ignore("WIP")]`).
  - **Часть Б (базовый случай Partial в ParseSeq + тай-брейк + guard нуля) — выполнено:**
    - **Partial base case в `ParseSeq`:** при сбое элемента Seq с прогрессом (`elemIdx > 0 && newPos > startPos`) возвращается `Result.Partial` вместо `Failure`; `ParseContext` строится с `SeqFrameLocation(elemIdx)` и `FirstSets.Get(element, …)`.
    - **`BuildPartialSeqNode(seq, elements, startPos, endPos)`:** helper — 1 элемент → сам элемент, иначе `SeqNode`.
    - **Тай-брейк в `ParseRule`:** при равной длине `Success` выигрывает у `Partial`; `bestResult` теперь проверяется **до** `_partialAccumulated` в конце `ParseRule`.
    - **Guard нуля в `OneOrMany`/`ZeroOrMany`:** `break` при `newPos == currentPos` (ε-элемент) — устраняет бесконечный цикл (тест `Test_EpsilonLoop_DoesNotHang`).
    - **`ParseContext?` nullable:** контекст цикла/Seq — `ParseContext?` (null при отсутствии Partial).
    - **`_lastPartial` / `LastPartial`:** хук наблюдаемости для тестов (аналог `LastSnapshot`); сбрасывается в `Parse()`; пишется в Partial base case `ParseSeq`.
    - **Ветка Partial в `ParseRule`:** больше не пишет в `_partialMemo` и не ставит `_partialAccumulated` (v1-путь `ContinueFromPartial` был мёртвым: Partial раньше нигде не порождался, только пропагация); `_partialAccumulated = null` сбрасывается в начале `ParseRule`. Полное удаление мёртвого кода — в 0.7.
    - **Судьба хака `Parser.cs:336-343`:** ОСТАВЛЕН. Удаление ветки `else if (postNewPos == maxPos && bestResult == null && isRecoveryPos)` сломало 7 тестов recovery (SeparatedList/MiniC: `func(1, )`, `func(1,2,3, )`, `func(1,)`, `func(1, ,)`, `func(1, , , 2)`, `Err_MissingSecondExpression` и др.) — хак покрывает Error-правила, восстанавливающие недописанные конструкции. Удаление переносится в 1.5 (S0-приоритет).
    - **Результаты:** сборка `Nitra.sln` — 0 ошибок; `PartialBaseCaseTests` — 5/5; регрессия `Tests/ParserTests` — **207 passed / 0 failed / 2 skipped**.

### 0.4 Терминальный кэш + слой инъекций + единый `TerminalComparer`
- [x] Статус — выполнен, проверен (TerminalCacheTests 4/4; ParserTests 211/211)
- **Что:** чистый `_terminalCache`, слой `_injections`, `ReportMismatch`; единый `TerminalComparer`; общий стабильный `EofTerminal`/`EmptyTerminal`-синглтон вместо приватных non-singleton'ов `FollowSetCalculator.cs:9-18`.
- **Файлы:** `Parser.cs`, `Recovery/TerminalComparer.cs`, `Recovery/Injection.cs`, `FollowSetCalculator.cs`
- **Тесты:** `TerminalCacheTests`: кэш mismatch'а стабилен; инъекция поверх кэша; идентичность `Literal` vs instance; EOF-синглтон идентичен между калькулятором и engine.
- **Заметки:**
  - **Синглтоны:** приватные `record EmptyTerminal()`/`record EofTerminal()` из `FollowSetCalculator` вынесены в `Recovery/TerminalComparer.cs` как `public sealed record EpsilonTerminal` (Kind `"ε"`) и `public sealed record EofTerminal` (Kind `"EOF"`), каждый с приватным конструктором и `static readonly Instance`. **Имена:** `EpsilonTerminal` (а не `EmptyTerminal`) — чтобы не конфликтовать с публичным `EmptyTerminal(string Kind) : RecoveryTerminal` из `Rules.cs`; `EofTerminal` — переиспользованное имя (приватный дубль удалён). `record` обязателен (CS8865: от record наследуется только record). `FollowSetCalculator` теперь использует `EpsilonTerminal.Instance`/`EofTerminal.Instance` (поведение не меняется; его приватный `TerminalEqualityComparer` оставлен — он сравнивает не-Literal по ссылке, что корректно для синглтонов).
  - **`TerminalComparer`:** `Instance` (`IEqualityComparer<Terminal>`) — Literal по `Value`, остальное (вкл. EOF/ε-синглтоны) по `ReferenceEquals`; `KeyComparer` (`IEqualityComparer<(int Pos, Terminal Terminal)>`) — `Pos` + `Instance`. Хэш ключа комбинируется вручную (`(Pos * 397) ^ h`), т.к. `System.HashCode.Combine` недоступен в netstandard2.0.
  - **`Injection`:** `public readonly record struct Injection(int Length, string NodeKind, bool IsSkip)` + `Insert(kind)` (Length 0) / `Absorb(kind, length)`.
  - **`Parser`:** добавлены `_terminalCache` и `_injections` (оба с `KeyComparer`); `ParseTerminal` — инъекция → `CreateInjectedResult`, затем кэш (miss → `TryMatch` → запись; mismatch `-1` кэшируется и не чистится в ходе прохода), `ReportMismatch(terminal, pos)` факторизован и вызывается ВСЕГДА при mismatch (и на cache hit); `CreateInjectedResult` — Length 0 → пустой `TerminalNode(IsRecovery: true)`, Length > 0 → абсорбер `[pos..pos+Length)`; убран комментарий `// ???` у `maxFailPos`.
  - **Решение (очистка инъекций):** `_terminalCache.Clear()` + `_injections.Clear()` в начале `Parse` (согласно плану §3.1: «инициализация: очистка _memo, _index, _injections, _lastSnapshot, _recoveryDiagnostics»). **Отклонение снято:** ранее `_injections` НЕ очищался, т.к. тест 2 делал `AddInjection` между двумя вызовами `Parse` (инъекция должна была пережить ре-парсинг). Тест 2 переделан на реальную модель engine Фазы 1 — **один вызов `Parse`**, где инъекция появляется в ходе парсинга (side-effect в `TryMatch` триггерного терминала → `AddInjection` для более поздней позиции; инъекция проверяется ПЕРЕД кэшем). Очистка в начале `Parse` не мешает: в Фазе 1 engine добавляет инъекции ВНУТРИ одного `Parse` (между итерациями ре-парсинга).
  - **Хук для тестов:** `public void AddInjection(Terminal, int pos, Injection)` (паттерн `LastSnapshot`/`LastPartial`; в проекте нет InternalsVisibleTo → public).
  - **Результаты:** сборка `Nitra.sln` — 0 ошибок (45 предупреждений — все предсуществующие); `TerminalCacheTests` — 4/4; регрессия `Tests/ParserTests` — **211 passed / 0 failed / 2 skipped** (207 базовых + 4 новых; 2 — предсуществующие `[Ignore("WIP")]`).

### 0.5 `FollowSetCalculator`: вложенные циклы, `GetTerminators(stack)`
- [x] Статус — выполнен, проверен (FollowSetTests 30/30; ParserTests 218/218)
- **Что:** единый обход дерева правила с накоплением «что следует после» (first/nullable suffix); `GetTerminators(stack)` (EOF — общий синглтон); компаратор.
- **Файлы:** `FollowSetCalculator.cs`, `Tests/ParserTests/Recovery/FollowSetTests.cs`
- **Тесты:** `FollowSetTests` (расширить: цикл в теле цикла; follow стартового правила содержит EOF-синглтон).
- **Заметки:**
  - **Подход (б) — точечная поправка через единый обход:** 4 метода `ProcessFollowSetForLoops*`/`ProcessFollowSetForRefsInRule*` (два независимых прохода, не спускались в вложенные циклы) заменены на `ProcessLoopSequence`/`ProcessLoopNode` с накоплением «что следует после». Для каждого элемента последовательности `WhatFollows(after, parentFollow)` = first(after) ∪ (after nullable ? parentFollow : ∅); `ProcessLoopNode` даёт всем Ref в теле цикла first(тело) ∪ separator ∪ whatFollows и **рекурсирует в тело** (Seq → `ProcessLoopSequence`, цикл → `ProcessLoopNode`) с `bodyFollow` = first(тело) ∪ whatFollows — так вложенный цикл получает корректный follow. Не рекурсируем в Ref-цели (каждое правило обрабатывается своим ходом во внешнем цикле) — нет зацикливания на взаимно-рекурсивных правилах.
  - **`GetTerminators(stack)`:** агрегация от ВНУТРЕННЕГО кадра (stack[last]) к внешнему (stack[0]); терминаторы кадра = `Options.Terminators ?? follow(правила кадра)`; дедупликация с сохранением порядка по `TerminalComparer.Instance`; EOF (`EofTerminal.Instance`) пропускается при агрегации и добавляется в конец — гарантия «EOF в конце», ровно один раз (даже если follow кадра уже содержит EOF).
  - **Результаты:** сборка `Nitra.sln` — 0 ошибок; `FollowSetTests` — 30/30 (23 базовых + 7 новых: 2 вложенных цикла + 5 `GetTerminators`); `Tests/ParserTests` — **218 passed / 0 failed / 2 skipped** (211 базовых + 7 новых; 2 — предсуществующие `[Ignore("WIP")]`); `RegexTests` 9, `WiWorkflowTests` 1 — все зелёные.

### 0.6 `RecoveryDiagnostic` + `Parser.RecoveryDiagnostics`
- [x] Статус — выполнен, проверен (ParserTests 220/220) — в работе (сборка 0 ошибок; RecoveryDiagnosticTests 2/2; ParserTests 220 passed / 0 failed / 2 skipped)
- **Что:** тип `RecoveryDiagnostic`, свойство `Parser.RecoveryDiagnostics` (пустой список в Фазе 0).
- **Файлы:** `Parser.cs`, `Recovery/RecoveryDiagnostic.cs`
- **Тесты:** `RecoveryDiagnosticTests`: список пуст (не null) после успешного и после неудачного парсинга.
- **Заметки:**
  - Создан `Recovery/RecoveryDiagnostic.cs` (`enum RecoveryKind { Inserted, Skipped, Unrecovered }` + `sealed record RecoveryDiagnostic(StartPos, EndPos, Kind, Message, Terminal?, RuleName?)`). В `Parser.cs`: `private readonly List<RecoveryDiagnostic> _recoveryDiagnostics = [];` + `public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics`; `_recoveryDiagnostics.Clear();` в `Parse` рядом с другими очистками. В Фазе 0 никто не добавляет — список всегда пуст.
  - **Результаты:** сборка `Nitra.sln` — 0 ошибок; `RecoveryDiagnosticTests` — 2/2; регрессия `Tests/ParserTests` — **220 passed / 0 failed / 2 skipped** (218 базовых + 2 новых; 2 — предсуществующие `[Ignore("WIP")]`).

### 0.7 Удаление мёртвого кода
- [x] Статус — выполнен, проверен (ParserTests 216/216; Фаза 0 завершена)
- **Что:** удалить `ContinueFromPartial`, `TryRecoverFromPartial`, `MergeWithPartialTree`, `ContinueFromPartialPostfixFallback`, `_partialAccumulated`, `_partialMemo`, `RecoveryStackReconstructor.cs` (+ его тесты), ветки `_partialMemo` в `ParseRule`/`Parse`; переименование `_recoverySkipPos` → `_recoveryPoint` (поведение RecoveryPrefix/Postfix сохраняется).
- **Файлы:** `Parser.cs`, `Recovery/`, `Tests/`
- **Тесты:** Регрессия: все существующие тесты ParserTests без изменений.
- **Заметки:**
  - **Удалено из `Parser.cs` (~230 строк):** методы `ContinueFromPartial`, `TryRecoverFromPartial`, `MergeWithPartialTree`, `ContinueFromPartialPostfixFallback` (целиком, с doc-комментами); поля `_partialMemo` и `_partialAccumulated`; `_partialMemo.Clear()` ×2 и `_partialAccumulated = null` в `Parse`/`ParseRule`; ветка `if (i == 0 && _partialMemo.Count > 0)` в `Parse`; ветка `if (isRecoveryPos && _partialMemo.TryGetValue(...))` в `ParseRule`; мёртвый хвост `if (_partialAccumulated is { } p)` в `ParseRule`. Комментарий «не паркуем в _partialMemo — … удаляется в 0.7» обновлён (теперь актуален).
  - **Удалён файл** `ExtensibleParser/Recovery/RecoveryStackReconstructor.cs` (в csproj явного include не было — глоб).
  - **Удалены тесты:** 4 `Test_StackReconstruction_*` из `Tests/ParserTests/Recovery/ParseContextTests.cs` (остальные тесты ParseContext сохранены).
  - **Переименование `_recoverySkipPos` → `_recoveryPoint`:** 16 строк в исходном `Parser.cs` (поле, `Parse` ×4, `ParseRule` ×4, `TryRecoverFromPartial` ×5 — метод удалён, `ContinueFromPartialPostfix` ×3, `ParseOftenMissed` ×1); после удаления живых ссылок — 11 строк. Чистое переименование, поведение RecoveryPrefix/Postfix и OftenMissed не изменилось.
  - **Сохранено намеренно (живой код):** memo-игнор в recovery-режиме (ветки `else if (_recoveryPoint == cached.MaxFailPos)` в `ParseRule` — станет основой Hygiene), RecoveryPrefix/RecoveryPostfix, OftenMissed, хак `bestResult == null && isRecoveryPos`, `ContinueFromPartialPostfix` (основной postfix-драйвер, не мёртвый — имя вводит в заблуждение, но из списка удаления не входит).
  - **Результаты:** сборка `Nitra.sln` — 0 ошибок (42 предупреждения — все предсуществующие); регрессия `Tests/ParserTests` — **216 passed / 0 failed / 2 skipped** (220 базовых − 4 удалённых теста реконструктора; 2 — предсуществующие `[Ignore("WIP")]`). Дерево на корректных входах не изменилось.

**Регрессия Фазы 0 (I6):** дерево на корректных входах не меняется (JSON, CppSimplified, DOT, CsNitra) — все существующие тесты зелёны.

---

## Фаза 1. Recovery engine (итеративный цикл)

### 1.1 Главный цикл §3.1
- [~] Статус — в работе (сборка 0 ошибок; IterativeRecoveryTests 6/6; ParserTests 222 passed / 0 failed / 2 skipped)
- **Что:** `RecoveryPointOf`, `FailureSnapshotAt`, `_recoveryPoint`, неявный S0-кандидат (ре-парсинг как есть: Hygiene без патчей), счётчики, fail-safe `ePrev`.
- **Файлы:** `Parser.cs`, `Tests/ParserTests/Recovery/IterativeRecoveryTests.cs`
- **Тесты:** `IterativeRecoveryTests`: один проход на одну ошибку; предельные случаи; существующие recovery-тесты MiniC (Error-правила) зелёны без изменений.
- **Заметки:**
  - **Цикл:** переписан по §3.1: `ePrev = -1`, `iter`-счётчик, `RecoveryPointOf` → fail-safe `e <= ePrev`, `iter >= MaxRecoveryIterations`, `_recoveryPoint = e`, S0-кандидат (`_attempts[e]` + `MaxRecoveryAttemptsPerPosition`), legacy-чистка memo как тело Apply S0, ре-парсинг, акцепт при `e2 > e` (I1) или Success@EOF, иначе break. Финал: лог "RULE STACK TRACE" + `MemoizationVisualazer` + `ErrorInfo = FatalError` (семантика §3.6 — в 1.4).
  - **`RecoveryPointOf` — отклонение от формулы §2 (решение):** для Success < EOF взято `max(NewPos, ErrorPos)`, а не `NewPos`. Проверено на практике (TRACE-лог MiniC `Err_MissingClosingBraceWithFunctionInside`): верхний результат — дегенеративный `Success@0` (пустой `ZeroOrMany`), тогда как реальная точка ошибки `ErrorPos` (mismatch `}` в 60). Формула `NewPos` даёт `e = 0` → S0-ре-парсинг с `_recoveryPoint = 0` не восстанавливает → тест MiniC падает. `max()` совпадает с `NewPos` для чистого хвостового мусора (там `ErrorPos <= NewPos`) и сохраняет legacy-поведление (ErrorPos) для дегенеративных Success'ов. Failure/Partial — как в §2.
  - **Ключевой механизм legacy-recovery (установлен экспериментально):** `NotPredicate(Ref(Function))` в Block спекулятивно парсит следующую функцию в pass 0 и кэширует её полный Success в `_memo`; S0-ре-парсинг с `_recoveryPoint` на точке разрыва вставляет `}`/`;` (OftenMissed), а следующая функция берётся из memo. Multi-pass-прогресс (I1) работает через вложенные recovery-успехи (RecoveryOperator-постфикс), чьи memo-записи (`pos != currentStartPos`, `NewPos != e`) переживают чистку.
  - **Legacy-чистка memo — оставлена как есть (в теле Apply S0):** удаляет `!IsSuccess` везде, `pos == currentStartPos`, `NewPos == e` (в старом коде — `NewPos == ErrorPos`; для Failure `e == ErrorPos`, идентично). Новая Hygiene (только Failure на e) — в 1.3.
  - **`FailureSnapshotAt(e)`:** возвращает `_lastSnapshot` iff `Pos == e`, иначе null (синтетический снимок для хвостового мусора — 1.2/S5). В 1.1 снимок не используется циклом (заготовка для engine'а).
  - **Счётчики:** `public int MaxRecoveryIterations { get; set; } = 64`, `public int MaxRecoveryAttemptsPerPosition { get; set; } = 3`, `private readonly Dictionary<int, HashSet<string>> _attempts` (очищается в начале `Parse`; в 1.1 e строго растёт, повторное посещение e недостижимо — проверка `_attempts[e].Contains("S0")` — защита). `GetOrAdd` недоступен в netstandard2.0 → `TryGetValue` + запись.
  - **Семантика ErrorInfo сохранена:** `FatalError(input, ErrorPos, PositionToLineCol, _expected)` в финале для невосстановленного результата; `PositionToLineCol`/`FatalError` не тронуты. Изменение семантики (Partial@EOF и т.д.) — 1.4.
  - **Тесты (6):** (1) пропущенная `;` → Success@EOF за один S0-проход; (2) пропущенная `}` → Success@EOF (восстановление через memo-кэш спекулятивного parse следующей функции); (3) 5 ошибок оператора + `MaxRecoveryIterations = 4` → завершается, не Success@EOF, `ErrorInfo != null` (каждая итерация двигает E: 17→29→41→53→65); (4) `MaxRecoveryIterations = 0` → один проход, немедленный выход; (5) корректный вход → Success@EOF, `RecoveryDiagnostics` пуст; (6) 5 ошибок оператора без лимита → Success@EOF (multi-pass, I1).
  - **Регрессия:** `Tests/ParserTests` — **222 passed / 0 failed / 2 skipped** (216 базовых + 6 новых; 2 — предсуществующие `[Ignore("WIP")]`); MiniC 36/36 без изменений; `RegexTests` 9/9, `WiWorkflowTests` 1/1.

### 1.2 `RecoveryEngine.Generate`
- [~] Статус — в работе (сборка 0 ошибок; CandidateGenerationTests 11/11 + AnchorResyncTests 3/3; ParserTests 236 passed / 0 failed / 2 skipped)
- **Что:** S1 (вставка), S2 (resync: T1 якоря / T2 CanStart, pre-filter по First, спекулятивная валидация с кэшем `(rule, pos)`, completion stack), S3 (токен-скан со вложенностью пар, кэшем, MaxSkip), S4 (EOF), S5 (хвост); детерминированная сортировка.
- **Файлы:** `Recovery/RecoveryEngine.cs`, `Recovery/RecoveryCandidate.cs`, `Recovery/ParseContext.cs` (поля `RecoveryOptions`), `Parser.cs` (хуки)
- **Тесты:** `CandidateGenerationTests` (per-стратегия + порядок + детерминизм); `AnchorResyncTests`: T1 находит следующий Member/Statement; T2 (CanStart) на двойной ошибке; completion stack вставляет `;`+`}`; мусор между e и якорем → абсорбер.
- **Заметки:**
  - **Создано:** `Recovery/RecoveryCandidate.cs` (record по §3.4) и `Recovery/RecoveryEngine.cs` — `public static class`, чистый генератор: side-эффекты на Parser'е только через Apply/Rollback кандидатов. `Generate(e, snapshot, input, parser, resultKind)` → детерминированно отсортированный список (§3.4.3: Rank, Cost, Pos, RuleName, TerminalKind — ordinal).
  - **`RecoveryOptions`** (`Recovery/ParseContext.cs`): добавлены `Anchors`/`CanStart`/`TryInsert`/`MaxSkip`/`Recoverable` (только данные; переезд в Rules.cs — 2.1).
  - **Хуки Parser:** `ApplyInjection`/`RollbackInjection` (oldValue null → удалить ключ), `SetMemo`/`RemoveMemo`/`PatchMemo` (все прецеденты `(pos, rule, prec)` в memo), read-only `Injections`/`Memo`, `FollowCalculator`, `GetTerminators(stack)`, `ParseRuleOnce` (прямой `ParseRule` без recovery-цикла). **Ключевое решение:** старые значения (инъекции/memo) захватываются в момент Generate (до любого Apply) — захват внутри Rollback-lambda не работает: Apply уже изменил состояние (поймано тестом S4 rollback).
  - **S1:** источники в порядке §3.4: `FailedTerminal` → `Expected` (топ кадра) → `TryInsert` (ближайший кадр с полем, ранг 0) → `FollowSet(top.RuleName)`; дедуп по `TerminalComparer`; EOF/ε-синглтоны никогда не вставляются; терминал, совпадающий в e (`TryMatch >= 0`) — кандидат не генерируется; cost 1; Apply = `Insert(T.Kind)` в `(e, T)`.
  - **S2:** T1-якоря = авторские (`Anchors` ближайшего кадра) + выводимые (кадры `LoopFrameLocation`: циклы `ZeroOrMany`/`OneOrMany`/`SeparatedList` с Ref-телом в правиле кадра, дедуп по имени, внутренние кадры первыми); T2 = только авторские `CanStart`. Pre-filter: ни один First-терминал не матчит в S → skip. Спекулятивный parse — одноразовый `Parser` (копия Rules/TdoppRules/Trivia, свой memo как кэш) + кэш `(rule, pos) → (ok, endPos)` на время Generate. T1 (ok && endPos > S) → кандидат, скан окончен; T2 (ok) → кандидат, скан продолжается. Completion stack: S > e → абсорбер `[e..S)` упавшего элемента (Ref → memo `Success(AbsorberNode)`, Terminal → injection `Absorb`), S == e → нулевые вставки First-терминалов упавшего элемента (совпадающие в e пропускаются); внешние Seq-кадры (наружу): не-nullable суффикс (i+1..end) → First-терминалы в S (совпадающие пропускаются), цикл/postfix-кадры обязательств не имеют. Cost = skip-cost + вставки + tier-penalty (T1: 0, T2: 1); одна диагностика Skipped/Inserted.
  - **S3:** терминаторы = `GetTerminators(snapshot.Stack)` (0.5); скан `e+1..min(e+MaxSkip, EOF)`; общий кэш `(pos, terminal) → len` на время Generate; вложенность пар: счётчики `{}`/`()`/`[]` по символам региона, закрывающая терминатор на глубине > 0 — «чужая», скан продолжается (счётчик гаснет, когда символ обрабатывается как часть региона на следующей итерации); EOF-терминатор «совпадает» только при S == EOF. Патч: элемент SeqFrameLocation → Ref → `PatchMemo` absorber для всех прецедентов в memo, Terminal → injection `Absorb`; fallback (локация не Seq / элемент не найден) → `Absorb` на `FailedTerminal`. Cost = слова + переносы в `[e..S)`; одна диагностика Skipped.
  - **S4:** S := input.Length; Seq-кадры (внутренние наружу): не-nullable суффикс после упавшего элемента → First-терминалы, вставляемые в S (для реальных терминалов в EOF матч всегда < 0); EOF не вставляется; cost = число вставок; диагностика на каждую. Без вставок — кандидата нет.
  - **S5:** только `resultKind == Success && e < EOF`. **Механизм (упрощение 1.2, задокументировано в коде):** инъекция `Absorb("Trailing", EOF-e)` в `(e, T)`, где T — первый терминал правила верхнего кадра (обход первой альтернативы с разрешением Ref'ов); для хвостового мусора это синтетический кадр start-правила (§3.4 S5) — снимок передаёт вызывающий (в 1.3 цикл сам соберёт его, имя start-правила в сигнатуре Generate нет); `snapshot == null` → кандидата S5 нет. Cost = слова + переносы в `[e..EOF)` + 1; одна диагностика Skipped.
  - **Упрощения (отмечены в коде):** `FindSeq` — первый Seq в альтернативах правила с числом элементов > elementIndex (вложенные Seq разрешаются первым совпадением — достаточно для патча упавшего/суффиксного элемента); якоря/CanStart поддерживаются только как `Ref` (не-Ref правила пропускаются — в 1.2 из тестов/авторских аннотаций якоря всегда Ref); `MaxSkip` — ближайший кадр с полем ?? 1000.
  - **Тесты (14):** `CandidateGenerationTests` (11): S1 вставка FailedTerminal/Expected в e + Apply/Rollback-инспекция инъекций; S1 TryInsert (ранг 0) + совпадающий в e терминал → нет кандидата; S4 суффикс в EOF + nullable-пропуск + нет кандидата без суффикса; S5 абсорбер `[e..EOF)` (только Success < EOF); S3 терминатор с учётом вложенности (чужая `}` внутри не стоп) + простой терминатор; порядок `(Rank, Cost, Pos, RuleName, TerminalKind)`; детерминизм (два Generate → один порядок Id'ов). `AnchorResyncTests` (3, MiniC-подобная грамматика Module/Function/Block/Statement/MemberStart): T1 Пример 1 §3.4 (пропущенная `}`: S == e, вставки `;`+`}`, cost 2); мусор `###` между e и якорем → S > e, абсорбер `[e..S)` + вставка `}`; T2 Пример 2 §3.4 (двойная ошибка: T1 в S == e не срабатывает, авторский `CanStart` через Options кадра снимка → кандидат T2 cost +1; скан продолжается и находит T1 дальше).
  - **Результаты:** сборка `Nitra.sln` — 0 ошибок (42 предупреждения — все предсуществующие); `Tests/ParserTests` — **236 passed / 0 failed / 2 skipped** (222 базовых + 14 новых; 2 — предсуществующие `[Ignore("WIP")]`).

### 1.3 Применение/откат, `Hygiene`
- [x] Статус — завершено (сборка 0 ошибок; PatchRollbackTests 6/6; ParserTests 242 passed / 0 failed / 2 skipped)
- **Что:** `MemoPatch`-лог, акцепт при E2 > E, `Hygiene` с обратным индексом `_index`.
- **Файлы:** `Recovery/MemoPatch.cs`, `Parser.cs`
- **Тесты:** `PatchRollbackTests`: откат восстанавливает memo/инъекции побайтово; префикс не тронут (I2); Hygiene удаляет Failure на e; интеграция S1; отклонённые кандидаты не оставляют следов.
- **Заметки:**
  - **Hygiene — итоговый scope: РАСШИРЕННЫЙ (задокументированное отклонение от узкого §3.5, см. `HygieneCore`).** Узкий вариант (только Failure на e для правил снимка + Failure start-правила на currentStartPos) НЕ проходит recovery-тесты: Error-правила/OftenMissed переиспытывают правила, упавшие в первом проходе на РАЗНЫХ позициях, а не только на e. Итоговый scope: (a) Failure на e для правил снимка (подмножество (c)); (b) start-правило на currentStartPos ЛЮБОГО типа (Partial/Success < EOF) — устаревший верхнеуровневый результат первого прохода, иначе re-парсинг читает его и не переиспытывает start-правило; (c) ВСЕ stale Failure (любая позиция) — только Failure, не Success/Partial. **Отклонение от I2:** удаляются Failure в префиксе `[currentStartPos, e)` и запись start-правила на currentStartPos — не удаётся избежать; Success/Partial префикса и Success-патчи, заканчивающиеся в e, не трогаются.
  - **MemoPatch-лог / Apply / Rollback:** `ApplyPatches` = `BeginPatchLog` + `candidate.Apply` + `HygieneCore` + `EndPatchLog` (патчи и Hygiene — атомарно, всё в лог). `RollbackPatches` — в обратном порядке: восстанавливает `OldValue` (или удаляет ключ, если `OldValue == null`); для memo — с восстановлением `_index`.
  - **Баг (фикс) — `RecordInjection`:** `Injection` — `readonly record struct`, поэтому при отсутствии ключа `TryGetValue` давал `default(Injection)`, который боксился в не-null `OldValue`; `RollbackPatches` вместо удаления ключа восстанавливал нулевой struct → фантомная инъекция `pos|Kind|0||False` после отката (ломал побайтовый откат). Фикс: записывать `null`, когда ключа не было (`had ? (object)old : null`). `Result` тоже struct, но в этих тестах memo-add-ветка (`RecordMemo` с новым ключом) не покрывается (кандидат S1 — только инъекция) — фикс намеренно минимальный, `RecordMemo` не тронут.
  - **PatchRollbackTests (6):** (1) `Test_Rollback_Restores_State_ByteForByte` — Apply кандидата → Rollback → memo+инъекции побайтово идентичны снимку; (2) `..._Unrecoverable` — то же для входа, который recovery не восстанавливает до EOF; (3) `Test_Prefix_Untouched_After_Apply` — префикс (pos < e) без изменений после Apply, кроме задокументированных удалений Hygiene (Failure + start-правило на currentStartPos); (4) `Test_Hygiene_Removes_Failure_At_E_Keeps_Success_Partial` — после Apply (S0, только Hygiene) Failure на e для правил снимка удалены, Success/Partial на e на месте; (5) `Test_S1_Integration_Insertion_Recovers` — S1 (вставка пропущенной `(`) восстанавливает до Success@EOF, в `RecoveryDiagnostics` есть Inserted; (6) `Test_Rejected_Candidates_Leave_No_Traces` — отклонённые кандидаты не оставляют лишних инъекций (каждой диагностике Inserted/Skipped соответствует инъекция; число инъекций ≤ числа принятых).
  - **Исправления тестов (тесты были неверны):** (3) и (4) сравнивали `List<string>` через `Assert.AreEqual` — это reference-equality (два разных инстанса списка никогда не равны) → `CollectionAssert.AreEqual` (сравнение по содержимому). В (3) дополнительно исключена из проверки запись start-правила на currentStartPos (задокументированное удаление Hygiene (b)).
  - **Регрессия:** `Tests/ParserTests` — **242 passed / 0 failed / 2 skipped** (Total 244; 2 — предсуществующие `[Ignore("WIP")]`). Фикс `RecordInjection` регрессии не дал (все ранее зелёные тесты остались зелёными).
  - Фикс 1.3.1 затронул интеграцию engine: в recovery-цикле добавлен откат `ErrorPos`+`_expected` вокруг re-parse кандидата (без прогресса → откат), иначе speculative-кандидаты S1..S5 загрязняли `_expected` (см. 1.3.1).

### 1.3.1 Разобраться со временем прохождения тестов (блокирующее, найдено при 1.3)
- [x] Статус — завершено (все тесты зелёные, прогон ~0.1с вместо ~4 мин)
- **Что:** `RequiredCallWith1Args/2Args/3Args` (MiniC) — OOM в StringBuilder тест-адаптера + прогон ParserTests растягивается до ~4 мин (остальные 233 теста зелёные и быстрые). Найти корень: подозрение на взрыв логов/кандидатов/спекулятивных parse'ов в recovery-цикле на этих входах. Цель: все тесты зелёные, прогон ParserTests < 1 мин.
- **Заметки:**
  - **Корень:** engine (1.3) применяет `Injection.Insert(",")` (нулевой матч) на разделителе `SeparatedList` в точке `e`; элемент списка матчит ε (`EmptyTerminal`/`ErrorEmpty`) в той же точке → итерация цикла `ParseSeparatedList` даёт нулевой прогресс (`currentPos` не сдвигается) → бесконечный цикл → тысячи ε-элементов → OOM в StringBuilder при рендере дерева. В `ParseOneOrMany`/`ParseZeroOrMany` guard нуля был (0.3), в `ParseSeparatedList` — нет.
  - **Фикс (Parser.cs):** (1) guard нуля в цикле `ParseSeparatedList`: после успешной итерации, если `newPos == iterStartPos` (ε-элемент/инъекция не сдвинули позицию) → `Log(High)` + `return Result.Failure(maxFailPos)` (не `break`: Failure делает `CallRequired` Partial, и longest-match/Success-beats-Partial выбирает префикс `Ident`=`func` — как в HEAD до engine); ε-элемент в список не добавляется. (2) В recovery-цикле: сохранение/откат `ErrorPos`+`_expected` вокруг re-parse кандидата (нет прогресса → откат) — иначе speculative-кандидаты S1..S5 загрязняли `_expected` (было `[`, Number, Ident, (]`, стало `[`,]`).
  - **Результат:** `ParserTests` — **236 passed / 0 failed / 2 skipped** (Total 238); прогон ~0.1 с (до: ~4 мин, 3 OOM). Логи: `t131_test1.log`, `t131_head_test1.log`, `t131_full.log` в temp.

### 1.3.2 Устранение багов и мелочей (по анализу качества 1.3)
- [ ] Статус
- **Что:** по результатам анализа качества 1.3: латентный баг `RecordMemo`, мёртвый обратный индекс, дыры в тестах, ленивый `Generate`.
- **Файлы:** `Parser.cs`, `Tests/ParserTests/Recovery/PatchRollbackTests.cs`

#### 1.3.2.1 Фантомный баг `RecordMemo`
- [x] Статус — завершено (failing→pass подтверждён; ParserTests 243 passed / 0 failed / 2 skipped)
- **Что:** `RecordMemo` (Parser.cs ~125-126): при отсутствии ключа `TryGetValue` → `default(Result)` боксуется в не-null `OldValue` → `RollbackPatches` восстанавливает фантомный нулевой `Result` вместо удаления ключа. Фикс: тот же паттерн, что в `RecordInjection` (`had ? (object)old : null`).
- **Тесты:** сначала failing-тест (кандидат с `SetMemo` на НОВЫЙ ключ → Apply → Rollback → в `Memo` нет фантома/ключа), затем фикс, затем полный прогон.
- **Заметки:**
  - **Failing-тест:** `PatchRollbackTests.Test_Rollback_Memo_NewKey_NoPhantom` — свежий `Parser`, кандидат (S2-подобный), чей Apply делает `SetMemo("PhantomRule", 5, 0, Result.Success(...))` на НОВЫЙ ключ → `ApplyPatches` → `RollbackPatches` → ассерт `Assert.IsFalse(Memo.ContainsKey(key))`. До фикса **УПАЛ** на этом ассерте: `Assert.IsFalse failed. Phantom default(Result) left in Memo after rollback of SetMemo on a new key` (ключ остался как фантомный `default(Result)`, Success/Node=null).
  - **Фикс:** `Parser.cs:125-126` — `var had = _memo.TryGetValue(key, out var old); log.Add(new MemoPatch(_memo, key, had ? (object)old : null, value));` (по образцу `RecordInjection`). После фикса откат нового ключа удаляет его из `_memo` (и `_index`), а не пишет фантом.
  - **Результаты:** failing→pass подтверждён; регрессия `Tests/ParserTests` — **243 passed / 0 failed / 2 skipped** (Total 245; 242 базовых + 1 новый; 2 — предсуществующие `[Ignore("WIP")]`). Фикс регрессии не дал (все ранее зелёные тесты остались зелёными).

#### 1.3.2.2 Обратный индекс `_index` (мёртвый код)
- [~] Статус — в работе (сборка 0 ошибок; ParserTests 243 passed / 0 failed / 2 skipped)
- **Что:** `_index` ведётся во всех точках мутации `_memo`, но `HygieneCore` идёт по всей таблице (`_memo.Keys.ToList()`), `GetPrecedences` нигде не вызывается. **Решение (уточнено):** УДАЛИТЬ `_index`/`IndexMemo`/`UnindexMemo`/`GetPrecedences` и все их вызовы. Обоснование: фактический scope Hygiene включает (c) «ВСЕ stale Failure в любой позиции» — он требует полного обхода таблицы ВСЕГДА, поэтому индекс не снижает сложность Hygiene (он был рассчитан на узкий scope §3.5). Подключение индекса для (a)/(b) не убирает полный обход из-за (c) — лишняя сложность без пользы. **Отклонение от §3.5** (узкий scope Hygiene, рассчитанный на индекс) зафиксировано в тексте пункта: фактический Hygiene шире (включает (c)), поэтому индекс бесполезен и удалён.
- **Тесты:** существующие `PatchRollbackTests` (поведение Hygiene) + регрессия (поведение не меняется — чистое удаление мёртвого кода).
- **Заметки:**
  - **Удалено из `Parser.cs` (~43 строки):** поле `_index` + 2 строки комментария; 3 метода `IndexMemo`/`UnindexMemo`/`GetPrecedences` + секция-заголовок `// ============ Обратный индекс memo (позиция → ключи) ============`; 8 точек мутации: `IndexMemo` ×5 (`SetMemo`, `PatchMemo`, `RollbackPatches`, `ParseRule`×2), `UnindexMemo` ×2 (`RemoveMemo`, `RollbackPatches`), `_index.Clear()` ×1 (инициализация `Parse`). Комментарий «Индекс восстанавливается» в `RollbackPatches` поправлен (индекса больше нет).
  - **Логика rollback'а не тронута:** в memo-ветке `RollbackPatches` сохранены восстановление/удаление ключа в `_memo` — удалены только вызовы `IndexMemo`/`UnindexMemo`.
  - **Ссылки:** после удаления `find_symbol_references` для `_index`/`IndexMemo`/`UnindexMemo`/`GetPrecedences` — 0 совпадений по коду (`GetPrecedences` был без вызовов — мёртвый).
  - **Результаты:** сборка `Nitra.sln` — 0 ошибок (все предупреждения предсуществующие: CS8602/CS8604/CS8600/CS8620/CS0618); диагностика `Parser.cs` — без новых предупреждений/ошибок; регрессия `Tests/ParserTests` — **243 passed / 0 failed / 2 skipped** (Total 245; 2 — предсуществующие `[Ignore("WIP")]`). Поведение не изменилось (чистое удаление мёртвого кода).

#### 1.3.2.3 Дыры в тестах (`PatchRollbackTests`)
- [ ] Статус
- **Что:** добавить тесты: (b) start-правило на currentStartPos любого типа удаляется Hygiene; (c) stale Failure в pos ≠ e удаляется; e2e: отклонённый S2/S3-кандидат с новым memo-ключом не оставляет следов в `Memo` (нет фантомов).
- **Тесты:** расширение `PatchRollbackTests`.

#### 1.3.2.4 Ленивый `Generate` после S0
- [ ] Статус
- **Что:** `Generate` вызывается каждую итерацию (Parser.cs ~426) — дорогие спекулятивные parse'ы, даже когда S0 сам восстанавливает. Порядок: сначала S0; `Generate` — только если S0 не дал прогресса (не Success@EOF и e2 <= e). Поведение (порядок акцепта) не меняется: S0 и так первый.
- **Тесты:** регрессия (все зелёные, поведение то же); по возможности — счётчик вызовов Generate (тест: вход, восстанавливаемый S0 за 1 итерацию → Generate 0 раз).

### 1.4 Семантика финала §3.6
- [ ] Статус
- **Что:** `ErrorInfo`/`RecoveryDiagnostics`, Partial-при-EOF как восстановленное.
- **Файлы:** `Parser.cs`
- **Тесты:** `FinalStateTests`
- **Заметки:**

### 1.5 `RecoveryRule`-развёртка в `ParseAlternative`
- [ ] Статус
- **Что:** развёртка (поведение = `Inner`, только чтение аннотаций, без `RecoveryOptions`-полных — они в Фазе 2); S0-кандидат в единой детерминированной сортировке.
- **Файлы:** `Parser.cs`
- **Тесты:** `S0IntegrationTests`: MiniC с Error-правилами; `RecoveryRuleTests.ParsesLikeInner`.
- **Заметки:**

**Интеграционные тесты Фазы 1:** пропущенная `;`, пропущенная `}` (вкл. вложенные), неожиданный токен в выражении, хвостовой мусор, несколько ошибок (проходы 1→2→3), корректный код без recovery-узлов (I6), «всё в дереве» (I4), детерминизм (I5).

---

## Фаза 2. Аннотации автора + качество

### 2.1 `RecoveryRule`/`RecoveryOptions`
- [ ] Статус
- **Что:** `RecoveryRule`/`RecoveryOptions` (§3.9) в `Rules.cs`; развёртка в `ParseAlternative`; engine читает опции из кадров (ближайший wins); вывод якорей из циклов.
- **Файлы:** `Rules.cs`, `Parser.cs`, `RecoveryEngine.cs`
- **Тесты:** `RecoveryRuleTests`: `Terminators` переопределяет follow; `Anchors` (авторские + выводимые, T1); `CanStart` (T2); `TryInsert`; `MaxSkip`; `Recoverable=false`.
- **Заметки:**

### 2.2 Единый cost model
- [ ] Статус
- **Что:** вставка 1; skip = слова+переносы; tier-penalty T1:0, T2:1; S4 = число вставок; `CountRecoveryNodes`.
- **Файлы:** `Recovery/CostCalculator.cs`
- **Тесты:** `CostModelTests`
- **Заметки:**

### 2.3 `OftenMissed` → сахар над `TryInsert`
- [ ] Статус
- **Что:** документированный сахар; поведенческая совместимость.
- **Файлы:** `Rules.cs`, `Parser.cs`
- **Тесты:** `OftenMissedTests` (расширить v1)
- **Заметки:**

### 2.4 Производительность
- [ ] Статус
- **Что:** бенчмарк «N ошибок → число проходов, время, размер memo»; оптимизация скана (дешёвые терминаторы первыми).
- **Файлы:** `Tests/ParserTests` (benchmark)
- **Тесты:** `RecoveryPerfTests`
- **Заметки:**

---

## Фаза 3. End-to-end и документация

### 3.1 E2E-набор на MiniC
- [ ] Статус
- **Что:** `Test_MissingClosingBrace_Function`, `Test_MissingSemicolon_MultipleStatements`, `Test_UnexpectedToken_Expression`, `Test_NestedErrors_MultipleBlocks`, `Test_TrailingGarbage`, `Test_Recovery_DoesNotBreakCorrectCode` (I6), `Test_EverythingRepresentedInTree` (I4), `Test_ParsingReachesEndOfString`, `Test_Deterministic` (I5), `Test_Anchor_Resync_NextMember` (T1), `Test_DoubleError_CanStart` (T2).
- **Заметки:**

### 3.2 Документация
- [ ] Статус
- **Что:** (а) гайд автора грамматики; (б) семантика восстановления; (в) известные ограничения.
- **Заметки:**

### 3.3 Опционально
- [ ] Статус
- **Что:** `ParseWithStackGuard`-аналог; `RecoveryEnabled=false` (полное отключение → однопассовый режим).
- **Заметки:**

### 3.4 Обновить чек-лист + пометить v1
- [ ] Статус
- **Что:** обновить `RecoverySystemChecklist.md` по Фазам v2; пометить `RecoverySystemPlan.md` (v1) как superseded.
- **Заметки:**
