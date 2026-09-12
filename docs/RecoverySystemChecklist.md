# Recovery System v2 — Implementation Checklist

**План:** `docs/RecoverySystemPlan.v2.md` (v1 — в `docs/Old/`, superseded).
**Тестирование:** на MiniC (грамматика `Tests/ParserTests/MiniC/`).
**Процесс:** каждый пункт — отдельный коммит. Субагент реализует один пункт, доводит до компилируемости, пишет тест к пункту, отлаживает, и дописывает в «Заметки» что сделал, с какими проблемами столкнулся и какие решения принял. Основной агент проверяет качество, помечает пункт `[x]` и коммитит.

**Легенда статуса:**
- `[ ]` — не начат
- `[~]` — в работе (субагент запущен)
- `[x]` — выполнен и проверен
- `[!]` — есть проблемы / отложено

**Текущий пункт:** 0.7

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
- [~] Статус — в работе (сборка 0 ошибок; ParserTests 216 passed / 0 failed / 2 skipped)
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
- [ ] Статус
- **Что:** `RecoveryPointOf`, `FailureSnapshotAt`, `_recoveryPoint`, неявный S0-кандидат (ре-парсинг как есть: Hygiene без патчей), счётчики, fail-safe `ePrev`.
- **Файлы:** `Parser.cs`
- **Тесты:** `IterativeRecoveryTests`: один проход на одну ошибку; предельные случаи; существующие recovery-тесты MiniC (Error-правила) зелёны без изменений.
- **Заметки:**

### 1.2 `RecoveryEngine.Generate`
- [ ] Статус
- **Что:** S1 (вставка), S2 (resync: T1 якоря / T2 CanStart, pre-filter по First, спекулятивная валидация с кэшем `(rule, pos)`, completion stack), S3 (токен-скан со вложенностью пар, кэшем, MaxSkip), S4 (EOF), S5 (хвост); детерминированная сортировка.
- **Файлы:** `Recovery/RecoveryEngine.cs`, `Recovery/RecoveryCandidate.cs`
- **Тесты:** `CandidateGenerationTests` (per-стратегия + порядок + детерминизм); `AnchorResyncTests`: T1 находит следующий Member/Statement; T2 (CanStart) на двойной ошибке; completion stack вставляет `;`+`}`; мусор между e и якорем → абсорбер.
- **Заметки:**

### 1.3 Применение/откат, `Hygiene`
- [ ] Статус
- **Что:** `MemoPatch`-лог, акцепт при E2 > E, `Hygiene` с обратным индексом `_index`.
- **Файлы:** `Recovery/MemoPatch.cs`, `Parser.cs`
- **Тесты:** `PatchRollbackTests`: откат восстанавливает memo побайтово; префикс не тронут (I2); hygiene удаляет только Failure на e.
- **Заметки:**

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
