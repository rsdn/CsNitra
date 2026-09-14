# Методы восстановления после ошибок (состояние на сегодня)

Краткое описание того, что реализовано в recovery-подсистеме `ExtensibleParser`.
Подробности: `RecoverySystemPlan.v2.md` (план), `RecoverySystemChecklist.md` (статус фаз),
`RecoveryAuthorGuide.md` (гайд автора грамматики), `RecoverySkippedTextPlan.md` (незавершённая Фаза 4).

## 1. Как устроено в целом

Recovery — внешний к парсингу итеративный процесс (инлайн-восстановление невозможно: при
longest-match падение отдельного правила не является ошибкой — ошибку достоверно сообщает
только верхний уровень, когда разбор не достиг EOF).

Цикл (`Parser.Recovery.cs`, `Recover`):

1. Пройти `ParseRule(startRule)` честно. Success@EOF — чистый успех, выход.
2. Иначе вычислить **точку восстановления E** (`RecoveryPointOf`): Failure → `ErrorPos`;
   Partial → `max(NewPos, ErrorPos)`; Success < EOF → `max(NewPos, ErrorPos)` (хвостовой мусор).
3. Снять/взять **`FailureSnapshot`** в E (стек правил, упавший терминал, ожидаемые).
4. Попробовать **кандидатов** восстановления: S0 всегда первым; S1..S5 генерируются
   лениво `RecoveryEngine.Generate` — только если S0 не дал прогресса.
5. Кандидат **принимается только при E2 > E** (I1, прогресс) или Success@EOF.
   Без прогресса — **откат** всех патчей по `MemoPatch`-логу и следующий кандидат.
6. Принятые патчи не откатываются — они становятся частью «восстановленной грамматики»
   (инъекции + memo-патчи), следующий проход парсит уже её. Префикс `[0..E)` берётся из
   `_memo` — повторный проход почти бесплатен.

Ограниченность (терминация): `MaxRecoveryIterations` (default 64) ×
`MaxRecoveryAttemptsPerPosition` (default 3, «предохранитель»; глубокие сценарии resync/panic/
trailing ставят его явно, например 16). Детерминизм: полная сортировка кандидатов
`(Rank, Cost, Pos, RuleName, TerminalKind)`.

Два предохранителя от зацикливания (Фаза 3.0a): zero-progress guard в циклах postfix/
`ParseSeparatedList` и guard глубины рекурсии (`_parseDepth`, лимит `input.Length*4+128`,
дефолт 4096).

## 2. Стратегии (кандидаты)

| Стратегия | Ранг | Что делает |
|---|---|---|
| **S0** | всегда первый | re-parse «как есть»: Hygiene без патчей; инлайн-механизмы грамматики срабатывают сами |
| **S1** | 0 / 1 | вставка ожидаемого терминала в точке E (missing token) |
| **S2** | 2 | resync к known-good точке: T1 (якоря) / T2 (`CanStart`) |
| **S3** | 3 | panic-скан до токен-терминатора |
| **S4** | 4 | достроение в EOF (суффиксные обязательства) |
| **S5** | 5 | хвостовой мусор: один абсорбер `[E..EOF)` |

### S0 — re-parse как есть (неявный кандидат)
Генерируется самим циклом, не engine'ом (`Apply` = только Hygiene). В ходе ре-парсинга с
`_recoveryPoint = E` срабатывают инлайн-механизмы грамматики:

- **TDOPP `RecoveryPrefix`/`RecoveryPostfix`** — альтернативы правила, доступные только в
  recovery-позиции (`PrefixesFor`/`PostfixesFor` с `isRecoveryPos`);
- **`OftenMissed(t)`** — инлайн-вставка нулевого `IsRecovery`-узла терминала `t`, если
  позиция = точка восстановления (`InsertOftenMissed`); дополнительно кадр несёт
  `TryInsert: [t]`, поэтому engine видит его и в S1 (ранг 0);
- **ε-принятие `RecoveryRule`** — нулевой прогресс альтернативы принимается только если она
  обёрнута в `RecoveryRule` (по умолчанию recoverable; `Recoverable: false` отключает).

Это декларативный аналог hand-written «костылей» Roslyn: автор описывает recovery правилом
грамматики, не кодом.

### S1 — вставка ожидаемого терминала (missing token)
Аналог Roslyn `EatToken` → `CreateMissingToken`. Источники терминалов (в порядке, с дедупом):
1. `FailedTerminal` снимка (ранг 1);
2. `Expected` верхнего кадра — First-множество упавшего элемента (ранг 1);
3. `TryInsert` из `RecoveryOptions` ближайшего кадра (ранг **0**);
4. `FollowSet` правила верхнего кадра (ранг 1).

Терминал, реально совпадающий в E, не вставляется; EOF/ε не вставляются никогда. Патч —
инъекция `Injection.Insert` в `(E, T)`: при ре-парсинге терминал матчит нулевую длину,
Seq продолжается со следующего элемента. Cost = 1. Диагностика `Inserted`:
`expected {kind}, found {preview}`.

### S2 — resync к known-good точке (структурная валидация)
Вместо гадания «где закрывающая» engine **проверяет** позиции `S` от E до `E+MaxSkip`
(default 1000) средствами грамматики: спекулятивный parse (изолированно, на одноразовом
scratch-парсере, с кэшем `(rule, pos)`).

- **T1 — якоря**: полный спекулятивный parse правила-якоря успешен с прогрессом → грамматика
  *доказывает* валидность точки. Якоря: авторские (`Anchors`) + **выводимые** из циклов
  (`ZeroOrMany`/`OneOrMany`/`SeparatedList` с `Ref`-телом в кадрах снимка → это `Ref`).
  T1 найден → скан окончен.
- **T2 — `CanStart`-предикаты** (только авторские): мягкий спекулятивный parse «может ли здесь
  начаться конструкция» — на случай, когда следующий код сам бит. Tier-penalty T2 = 1.

**Completion stack** (набор патчей кандидата):
- `S > E` — **абсорбер** `[E..S)` для мусора между разрывом и known-good точкой.
  Если падение в начале итерации цикла и правило цикла = якорь — абсорбер ставится
  **на уровне цикла** (memo-патч правила в E: цикл пропускает регион целиком, следующая
  итерация стартует с чистого терминала); иначе — memo-патч/инъекция на упавший элемент;
- `S == E` — нулевые вставки First-терминалов упавшего элемента (чистый missing token);
- внешние Seq-кадры (наружу) вносят **суффиксные обязательства**: не-nullable элементы
  после упавшего → их First-терминалы вставляются в S (совпадающие в S пропускаются).

Cost = skip-cost + число вставок + tier-penalty. Одна диагностика `Skipped`/`Inserted`.

### S3 — panic-скан до токен-терминатора
Терминаторы — агрегированный follow-стек `GetTerminators(stack)` (per-кадр
`Options.Terminators ?? follow(правила)`, от внутреннего кадра к внешнему, EOF в конце).
Скан текста `E+1..E+MaxSkip`:

- учитывается **вложенность пар** `{}()/[]` — «чужая» закрывающая внутри региона
  не останавливает скан;
- терминаторы пробуются **дешёвые первыми** (single-char Literal → остальные Literal →
  regex/DFA; детерминированная стабильная сортировка), EOF — только при `S == EOF`.

Патч: абсорбер `[E..S)` — на уровне цикла (для true-EOF-хвоста: падение в начале итерации
цикла, терминатор EOF), иначе memo-патч `Skipped`-узла на упавший `Ref`-элемент, иначе
инъекция `Absorb` на упавший терминал. Cost = слова + переносы в `[E..S)`.
Диагностика `Skipped`: `skip to terminator {kind}`.

### S4 — достроение в EOF
`S := input.Length` — валидна по определению. Для Seq-кадров снимка (внутренние наружу):
не-nullable суффикс после упавшего элемента → их First-терминалы вставляются в EOF
(EOF/ε не вставляются). Без вставок кандидата нет. Cost = число вставок.
Диагностика `Inserted`: `insert {kind} at EOF`.

### S5 — хвостовой мусор
Только для `Success < EOF`. Один абсорбер `[E..EOF)` kind `"Trailing"` — инъекция
`Injection.Absorb` на первый терминал правила верхнего кадра: ре-парсинг в E находит
абсорбер и покрывает хвост одним `IsRecovery`-узлом. Cost = skip-cost + 1.
Диагностика `Skipped`: `trailing garbage: {n} chars`.

## 3. Механизмы, на которых всё держится

- **`FailureSnapshot`** (`Recovery/FailureSnapshot.cs`) — живой снимок стека в момент самого
  дальнего mismatch: `StackFrame[]` (имя правила, прецедент, локация
  `RuleFrameLocation`/`SeqFrameLocation`/`LoopFrameLocation`/`PostfixFrameLocation`,
  `Expected`, `RecoveryOptions`), упавший терминал, ожидаемые терминалы. Один `_lastSnapshot`,
  перезаписывается при каждом новом дальнем mismatch.
- **Слой инъекций** (`Recovery/Injection.cs`) — `Dictionary<(pos, Terminal), Injection>`
  поверх чистого `_terminalCache` (mismatch кэшируется и не чистится). `Injection.Insert`
  (Length 0) / `Injection.Absorb` (Length > 0). `CreateInjectedResult`: Length 0 → пустой
  `TerminalNode(IsRecovery: true)` (вставка); Length > 0 → абсорбер
  `TerminalNode(kind, E, E+Length, Length, IsRecovery: true)` — **отдельный** узел в дереве.
- **Hygiene** (при каждом Apply, атомарно с патчами): удаляет из `_memo` ВСЕ stale
  `Failure` (любая позиция) и запись start-правила на `currentStartPos` любого типа
  (расширенный scope, задокументированное отклонение от узкого §3.5 плана); Success/Partial
  префикса не трогаются.
- **`MemoPatch`-лог** (`Recovery/MemoPatch.cs`) — все мутации memo/инъекций кандидата
  записываются (старое значение или null), откат восстанавливает их побайтово.
- **Изоляция спекулятивных парсингов** — `Speculative`: на время parse подавлены побочные
  эффекты (`ErrorPos`/`_expected`/снимок не двигаются), после — восстановлены.
- **Единая идентичность терминалов** (`Recovery/TerminalComparer.cs`) — `Literal` по Value,
  остальное по ссылке; стабильные синглтоны `EofTerminal`/`EpsilonTerminal`.
- **`FirstSets` / `FollowSetCalculator`** — First/Nullable-анализ правил и follow-множества
  (вкл. вложенные циклы), агрегация терминаторов по стеку кадров.
- **`CostCalculator`** — единый cost: вставка = 1; skip = число небелых «слов» + переводов
  строк; tier-penalty T1:0 / T2:1. Это сравнительная эвристика, не точная стоимость.
  `CountRecoveryNodes(root)` — метрика числа `IsRecovery`-узлов в дереве.
- **Диагностика** (`Recovery/RecoveryDiagnostic.cs`) — `RecoveryDiagnostics` накапливает
  принятые восстановления: `Inserted`/`Skipped` с позициями и сообщениями (таблица в
  `RecoveryAuthorGuide.md` §(б)). `Unrecovered` в enum есть, движком не порождается.

## 4. Аннотации автора грамматики

Обёртка `RecoveryRule(inner, options)` (`Rules.cs`) — поведение парсинга в точности
`Inner`; опции читает только recovery-движок из кадра снимка (ближайший кадр wins;
`Recoverable: false` отключает опции кадра и ε-принятие):

| Поле `RecoveryOptions` | Используется в |
|---|---|
| `TryInsert` | S1 (ранг 0) — терминалы для вставки |
| `Terminators` | S3/S4 — переопределяют follow кадра |
| `Anchors` | S2 (T1) — расширяют выведенные якоря |
| `CanStart` | S2 (T2) — только авторские, не выводятся |
| `MaxSkip` | S2/S3 — лимит скана (default 1000) |
| `Recoverable` | opt-out правила из восстановления |

Плюс «сырые» механизмы: `OftenMissed(t)` (сахар над `TryInsert` + инлайн-вставка в S0),
TDOPP `RecoveryPrefix`/`RecoveryPostfix`, `RecoveryTerminal` (терминал с `IsRecovery`).

## 5. Семантика результата

- **Дерево**: вставленный токен = пустой `TerminalNode(ContentLength: 0, IsRecovery: true)`;
  пропущенный текст = отдельный узел-абсорбер `TerminalNode("Skipped"/"Trailing", E, S, S-E,
  IsRecovery: true)`. I4: каждый символ входа входит ровно в один терминальный узел (тайлинг
  `[StartPos, EndPos)`). I6: на корректном коде recovery латентен — 0 recovery-узлов.
  > Фаза 4 (`RecoverySkippedTextPlan.md`): встраивание грязи в предыдущий `TerminalNode`
  > (fold) — **ещё не реализована**, абсорберы пока отдельные узлы.
- **Финальные состояния** (`FinalizeResult`): Success@EOF / Partial@EOF → восстановлено
  (`ErrorInfo = null`); Success < EOF / Failure / Partial < EOF после исчерпания кандидатов →
  `ErrorInfo = FatalError` в последней точке восстановления E.
- **`Parser.RecoveryDiagnostics`** — принятые восстановления; `Parser.LastSnapshot` /
  `LastPartial` — наблюдаемость; `RecoveryPasses` / `EngineGenerateCalls` — счётчики.

## 6. Режим без recovery

`EnableRecovery=false` (переменная MSBuild, `Directory.Build.props`; по умолчанию true):
не определяется символ `RECOVERY`, активен `Parser.NoRecovery.cs` — все хуки тривиальны
(no-op/константы, JIT сворачивает в ноль), состояния recovery-подсистемы нет: один честный
проход, падение фиксируется как `FatalError`.
