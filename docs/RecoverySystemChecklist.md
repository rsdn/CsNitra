# Recovery System v2 — Implementation Checklist

**План:** `docs/RecoverySystemPlan.v2.md` (v1 — в `docs/Old/`, superseded).
**Тестирование:** на MiniC (грамматика `Tests/ParserTests/MiniC/`).
**Процесс:** каждый пункт — отдельный коммит. Субагент реализует один пункт, доводит до компилируемости, пишет тест к пункту, отлаживает, и дописывает в «Заметки» что сделал, с какими проблемами столкнулся и какие решения принял. Основной агент проверяет качество, помечает пункт `[x]` и коммитит.

**Легенда статуса:**
- `[ ]` — не начат
- `[~]` — в работе (субагент запущен)
- `[x]` — выполнен и проверен
- `[!]` — есть проблемы / отложено

**Текущий пункт:** 0.2

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
- [ ] Статус
- **Что:** `CaptureSnapshot` в точке самого дальнего mismatch, `_lastSnapshot`, `Speculative`-хелпер, изоляция предикатов + speculative-проверки `SeparatedList`.
- **Файлы:** `Parser.cs`, `Recovery/FailureSnapshot.cs`
- **Тесты:** `SnapshotTests`: снимок при самом дальнем mismatch; спекуляция не портит снимок/`ErrorPos`/`_expected`.
- **Заметки:**

### 0.3 Базовый случай `Partial` в `ParseSeq`
- [ ] Статус
- **Что:** базовый случай Partial + `FirstSets.Get` + тай-брейк (Success бьёт Partial при равной длине) + guard нуля в `OneOrMany`/`ZeroOrMany` (break при `newPos == currentPos`) + удаление хака `Parser.cs:336-343`.
- **Файлы:** `Parser.cs`, `Recovery/FirstSets.cs`
- **Тесты:** `PartialBaseCaseTests`: `int x int y;` даёт Partial с `SeqFrameLocation(2)`; ε-цикл не виснет; тай-брейки.
- **Заметки:**

### 0.4 Терминальный кэш + слой инъекций + единый `TerminalComparer`
- [ ] Статус
- **Что:** чистый `_terminalCache`, слой `_injections`, `ReportMismatch`; единый `TerminalComparer`; общий стабильный `EofTerminal`/`EmptyTerminal`-синглтон вместо приватных non-singleton'ов `FollowSetCalculator.cs:9-18`.
- **Файлы:** `Parser.cs`, `Recovery/TerminalComparer.cs`, `Recovery/Injection.cs`, `FollowSetCalculator.cs`
- **Тесты:** `TerminalCacheTests`: кэш mismatch'а стабилен; инъекция поверх кэша; идентичность `Literal` vs instance; EOF-синглтон идентичен между калькулятором и engine.
- **Заметки:**

### 0.5 `FollowSetCalculator`: вложенные циклы, `GetTerminators(stack)`
- [ ] Статус
- **Что:** единый обход дерева правила с накоплением «что следует после» (first/nullable suffix); `GetTerminators(stack)` (EOF — общий синглтон); компаратор.
- **Файлы:** `FollowSetCalculator.cs`
- **Тесты:** `FollowSetTests` (расширить: цикл в теле цикла; follow стартового правила содержит EOF-синглтон).
- **Заметки:**

### 0.6 `RecoveryDiagnostic` + `Parser.RecoveryDiagnostics`
- [ ] Статус
- **Что:** тип `RecoveryDiagnostic`, свойство `Parser.RecoveryDiagnostics` (пустой список в Фазе 0).
- **Файлы:** `Parser.cs`, `Recovery/RecoveryDiagnostic.cs`
- **Тесты:** —
- **Заметки:**

### 0.7 Удаление мёртвого кода
- [ ] Статус
- **Что:** удалить `ContinueFromPartial`, `TryRecoverFromPartial`, `MergeWithPartialTree`, `ContinueFromPartialPostfixFallback`, `_partialAccumulated`, `_partialMemo`, `RecoveryStackReconstructor.cs` (+ его тесты), ветки `_partialMemo` в `ParseRule`/`Parse`; переименование `_recoverySkipPos` → `_recoveryPoint` (поведение RecoveryPrefix/Postfix сохраняется).
- **Файлы:** `Parser.cs`, `Recovery/`, `Tests/`
- **Тесты:** Регрессия: все существующие тесты ParserTests без изменений.
- **Заметки:**

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
