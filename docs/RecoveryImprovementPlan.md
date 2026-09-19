# План улучшений системы восстановления ExtensibleParser (CsNitra)

Цель (оговорена): **стопроцентный разбор любого файла до EOF + высокая эффективность
recovery для живого редактирования в IDE**. Идея recovery — из Roslyn (рукописный LL-парсер:
инлайн-чинка, гарантированное дно, диагностика на дереве), адаптированная под grammar-driven
движок с расширяемой на лету грамматикой и внешним итеративным re-парсом.

База плана: `RecoveryImprovementProposal.md` (предложения A1–A3, B1–B5, C1–C2, D1–D2, E,
R1–R6 — далее «база»). Настоящий план добавляет к ней согласованные пункты из анализа
`antlr4-analysis.md`: **A4-1…A4-7** (из ANTLR4), **A5-1…A5-8** (из Roslyn + решения §9:
per-call-site FOLLOW, паник-дно, K-кандидаты) и уточнение A5-1 (T1-якоря для любого
Ref-кадра). Нумерация не пересекается.

## Исполнение (обязательно перед стартом)

Исполнитель плана **обязан** прочитать скилл `plan-execution`(`C:\Users\user\.config\opencode\skill\plan-execution\SKILL.md`) **до начала выполнения**
и следовать ему. Если скилл не в контексте или не помнится — **прочитать его заново**,
не полагаться на память.

## Принципы (не нарушать)

1. **Work-bound, а не time-bound**: число операций на точку/итерацию ограничено
   детерминированно; time-budget — только как деградация сверху (база B4).
2. **Гарантированное дно**: в IDE-профиле всегда существует кандидат, доводящий до EOF
   (S6-дно, п. 1 ниже). `FatalError` — только в Compiler-профиле.
3. **Состояние не отравляется**: откат по `PatchLog`; мемо-инвалидация точная (B1).
4. **Ошибка — событие, а не приговор**: после любой ошибки разбор продолжается до конца
   файла; каждая ошибка — отдельная диагностика (A4-2).
5. **Нулевая цена на корректном коде** (I6): recovery-механики генерируются только при ошибке.
6. **Чего не делаем** (зафиксировано в `antlr4-analysis.md` §6.9, §3 «чего не брать»):
   глобальный чарт/fixpoint по ценам, hand-written стоп-предикаты, поток токенов с O(1)-откатом
   (непереносимо при longest-match — re-парс + memo это корректная модель), two-stage SLL/LL,
   trivia-представление пропущенного.

## Волны

Порядок волн: **0 → 1 → 2 → 3 → 4 → 5 → 6 → 7**. Волна 0 ставится первой, чтобы всё
остальное измерять по времени, а не по интуиции (рекомендация базы).

---

### Волна 0 — измерительная база (база D1, D2-ядро)

| # | Что | Где | Приёмка |
|---|---|---|---|
| D1 | Бенчмарк-корпус: 7 сценариев (100+ мелких ошибок; одна ошибка в конце большого файла; ошибка в вложенных циклах/в начале итерации; regex-терминалы; амбивалентный longest-match; «грязный» файл с ошибками каждые N строк; **D1.7: мусор + битая следующая конструкция — «двойное повреждение», единственный кейс, где T2/A5-1 дают выигрыш (T1 не проходит, S3 останавливается слабо); метрика: resync в старт битой конструкции, а не на слабый терминатор**) | `Tests/` + `docs/` (описание корпуса) | Ассерты на время recovery на файл; замеры до/после каждой волны; D1.7 — приёмочный тест ценности волны 5 |
| D2-ядро | Счётчики: `RecoveryPasses`, `EngineGenerateCalls` (уже есть, `Parser.Recovery.cs:25-29`) — вынести в отчёт бенчмарка | `Parser.Recovery.cs` | Видно число проходов/генераций на сценарий |

---

### Волна 1 — контракт и дно: «всегда до конца + все ошибки»

Главная тема плана. После неё IDE-профиль никогда не «сдаётся».

| # | Что | Где | Приёмка |
|---|---|---|---|
| A1 (база) | Kандидат последней инстанции **S6** «гарантированный прогресс»: генерируется всегда при `E < EOF`, **включая `snapshot == null`**; абсорбер `[E..S)` до known-good точки или EOF; при падении в `Ref` — memo-патч, на уровне цикла — абсорбер цикла, иначе — Absorb-инъекция; при `snapshot == null` — синтетический кадр start-правила | `Recovery/RecoveryEngine.cs` (новый `GenerateS6`, ранг 6), `Parser.Recovery.cs` (цикл `Recover`, 261-357) | Тест: `Module := Expr Expr`, вход `12 34 56 78` → Success@EOF, хвост покрыт; единственный выход с `FatalError` — `Recoverable: false` |
| **A5-7** | **S6 = паник-дно** (уточнение A1, решение §9): стоп-набор S6 = **терминаторы** (FOLLOW-объединение по стеку — см. A5-6; fallback rule-level) ∪ anchor-First (текущий дешёвый First-скан A1); **без требования snapshot**; **без MaxSkip** (или предел из профиля); строгие регионы — исключение (C1) | `Recovery/RecoveryEngine.cs` (S6), `FollowSetCalculator.cs:230-247` | Тест: хвостовой мусор без mismatch на границе (snapshot==null) → дно работает; регион > 1000 символов → дно работает; `}` внутри строки не ломает скан (связка с R1/B3) |
| **A4-2** | **«Отчёт и продолжить»**: при исчерпании лимитов (64 итерации / 3 попытки на точку, `Parser.Recovery.cs:286-290`) — не `FatalError`-стоп, а: (1) `RecoveryDiagnostic(Kind.Unrecovered, E, ...)` (значение enum уже есть, `Recovery/RecoveryDiagnostic.cs`, просто не генерируется), (2) дно S6, (3) продолжение итераций — следующие ошибки файла обрабатываются так же. Итог: **список** diagnostics + дерево по всему вводу | `Parser.Recovery.cs` (`Recover`, `FinalizeResult` 238-258), `Recovery/RecoveryDiagnostic.cs` | Тест: файл с 3+ ошибками, вторая «неспастимая» в пределах лимитов → обе отчитаны, разбор дошёл до EOF; `Unrecovered` присутствует; **корпус D1.1 (100+ ошибок) завершается в IDE-профиле** — дефолт `MaxRecoveryIterations=64` (`Parser.Recovery.cs:32`) мал (100 ошибок × ≥1 итерация > 64) → вынести в `RecoveryProfile` (A3) / поднять для IDE |
| **A5-4** | **Дно-контракт результата**: в IDE-профиле итог = `Success<T>` с деревом, покрывающим весь ввод (абсорберы по I4), а не `Failed(FatalError)` (`ParseResult.cs:14-16`); `Failed` остаётся только в Compiler-профиле (A3/A4-5); диагностика `Unrecovered`/`InsufficientStack` на дереве (A5-2). Триггеры дна: исчерпание recovery, срабатывание depth-guard (R3 — *предварительное* `InsufficientExecutionStackException`; реальный stack overflow неперехватываем и является багом, устраняемым калибровкой guard'а) | `Parser.Recovery.cs` (`FinalizeResult`), `ParseResult.cs`, консьюмеры (`CsNitraParser.cs:140-153` — убрать `InvalidOperationException` на Partial@EOF) | Контракт-тесты (C1): для любого входа IDE-профиль возвращает `Success<T>`; корневой узел start-правила всегда есть |
| C1 (база) | Аудит гарантии прогресса (I1): из `Recover` с `FatalError` возможен выход только при `Recoverable: false`; взаимодействия S6+MaxSkip (S6 игнорирует MaxSkip — дно), S6+вложенные строгие регионы | тесты | Зелёные контракт-тесты |
| C2 (база) | Семантика Partial после recovery: S6-абсорберы дают `Success@EOF` (не Partial); зафиксировать | тесты | Partial@EOF с диагностикой недостижим (или имеет явный смысл) |

**Критерий волны:** в IDE-профиле на любом входе из D1-корпуса — полное дерево + список
диагностики; `FatalError` отсутствует.

---

### Волна 2 — скорость re-парса (база B1, A2)

| # | Что | Где | Приёмка |
|---|---|---|---|
| B1 (база) | **Точная hygiene** (главный выигрыш): `HygieneCore` (`Parser.Recovery.cs:445-450`) удаляет не все `Failure`, а только `Failure` с `pos < E && MaxFailPos >= E` + запись start-правила на `currentStartPos`. `MaxFailPos` уже в `Result.cs:13` — проверка бесплатна | `Parser.Recovery.cs` | Тест «одна ошибка в конце большого файла»: число удалений memo до/после (D2); время re-парса падает |
| A2 (база) | **Бюджеты по тирам**, а не по кандидатам: S1 — под-бюджет (напр. 4 вставки); **S2 — собственный под-бюджет (отдельно от S3/S6** — иначе щедрый T2, ранг 2 < 3, съедает общий бюджет и S3/S6 не пробуются; см. A5-1); S3/S6 — отдельный под-бюджет; «попыткой» считать тир. Параметр в `RecoveryProfile` (A3) | `Parser.Recovery.cs` (`TryCandidate`, 306-339) | S1 не выедает весь дефолтный бюджет 3 раньше S2/S3/S6; **S2-бюджет не глушит S3 (регресс-тест: «мусор с identifier-токенами» → S3 всё равно пробуются)** |

**Критерий волны:** сценарий D1.2 (ошибка в конце файла) — re-парс префикса почти чистыми
memo-hit'ами; время на D1-корпусе не хуже до-волны.

---

### Волна 3 — класс «ничего не совпало» (база R1)

| # | Что | Где | Приёмка |
|---|---|---|---|
| R1 (база) | **Мусорный терминал** (аналог `BadToken` Roslyn): встроенный `Garbage`, `TryMatch(p)` = «ни один терминал набора не совпал в p», потребляет до первой позиции, где кто-то совпадает, или до лимита (64 символа / 16 токенов). Класс сценариев «битые символы, чужой синтаксис, обрезанный файл» — из Failure в локальный «съел мусор» (уровень S0/S1) | `Rules.cs` (новый терминал), `Parser.cs` (`ParseTerminal` 754-791) | Тест: вход с подряд идущими несуществующими символами → локальный абсорбер, не взрывной S2/S3/S6 |

**Критерий волны:** сценарии D1 с «битыми» регионами не доходят до S3/S6.

---

### Волна 4 — профили и предсказуемость (база A3, B4 + A4-5)

| # | Что | Где | Приёмка |
|---|---|---|---|
| A3 (база) | **`RecoveryProfile`**: `Mode (Ide\|Compiler\|Test)`, `TimeBudget`, `MaxIterations`, `AttemptsPerTier`, `MaxSkip`, `StrategyMask (S0..S6)` — стратегия, бюджеты и жёсткость результата настраиваются на парсер, а не на сборку | новый тип + `Parser` (конструктор) | IDE: большой бюджет, S6 обязателен, деградация вместо `FatalError`; Compiler: маленький бюджет, быстрый fail |
| **A4-5** | **Runtime-стратегия вместо `#if RECOVERY`** (аналог `BailErrorStrategy` ANTLR4): `Mode.Compiler` = **bail** — без `RecoveryEngine`, без кандидатов, первый `FatalError`/`Recoverable:false` завершает парсинг сразу. Устраняет compile-time развилку (`Directory.Build.props:6-12`) и её побочный эффект: в no-recovery режиме **пропал depth-guard** (`Parser.NoRecovery.cs:45`) — в runtime-режиме guard сохраняется. Two-stage для компилятора: bail-проход → при ошибке recovery-проход того же файла (на корректном коде recovery не запускается — латентность нулевая) | `Parser.NoRecovery.cs` → заменить runtime-ветвями, `Parser.Recovery.cs` | Одна сборка покрывает IDE/компилятор/тесты; depth-guard активен во всех режимах (тест R3-репро) |
| B4 (база) | **Time-budget с лестницей деградации**: (1) полное: S0–S6 со спекуляциями; (2) выключить S2-спекуляции (только First-скан), MaxSkip ×4; (3) только S1 + S3-короткий + S6; (4) жёсткий предел: принять S6 и остановиться — дерево полное, диагностика есть | `RecoveryProfile`, `Recover` | Худшая IDE-задержка ограничена конструктивно; «не восстановилось» → «восстановлено грубо», не `FatalError` |

**Критерий волны:** худшее время на D1-корпусе ограничено профилем; Compiler-проход на
корректном коде — без recovery-работы (замер).

---

### Волна 5 — дешёвые кандидаты, границы, точный FOLLOW

Самая «/antlr4-ная» волна: всё, что делает обнаружение и чинку ошибок дешевле и точнее.

**Порядок внутри волны (зависимости):** B2 (specCache) → B3 (дешёвые сканы) → A4-1/A4-3/A4-4 → A5-6 → A5-1 → R2. A5-1 зависит от B2+B3+A5-6 (+ A2, волна 2); R2 и A4-7 потребляют per-site (A5-6). **Расщепление для снижения риска:** **5a** (дешёвые: B2, B3, A4-x) → **5b** (A5-6, A5-1, R2).

| # | Что | Где | Приёмка |
|---|---|---|---|
| **A4-1** | **«Тихая зона»** (аналог `errorRecoveryMode`/`reportMatch`): после принятия кандидата в точке E mismatch'ы с позицией **≤ E** не обновляют `ErrorPos`/`_expected` и не снимают snapshot (они устарели — их чинит патч); новый `FailureSnapshot` — только строго дальше E | `Parser.Recovery.cs` (`CaptureSnapshot` 486-499, поле `_recoveryPoint` 15), `Parser.cs` (`ReportMismatch` 794-808) | Тест: после принятого патча преждевременный mismatch на дальней позиции (артефакт) не перетирает `_expected`; snapshot'ов за итерацию меньше (D2) |
| **A4-3** | **Single-token deletion как кандидат ранга 1** (аналог `singleTokenDeletion` ANTLR4): если терминал, следующий за E (после trivia), ∈ ожидаемому в E (снимок: `Expected`/`TryInsert`/`FailedTerminal`) — абсорбер ровно `[E..E+1)` без спекулятивного parse. Класс «лишний токен» закрывается на ранге 1 за O(1) | `Recovery/RecoveryEngine.cs` (новый генератор между S1, 38-98, и S2, 104-298; механика Absorb — 191-195) | Тест: `aab`-кейсы (дубликат токена) → одна диагностика `extraneous`, ранг 1, без S2 |
| **A4-4** | **First-префильтр и ожидаемые на границе** (аналог `sync` ANTLR4): (1) в `ParseAlternative` (`Parser.cs:490-523`) перед попыткой альтернативы — First-проверка первого терминала/элемента (terminal cache, `Parser.cs:759-763`, делает проигрыш бесплатным — убираем кадры и snapshot-шум); (2) в начале итерации `ZeroOrMany`/`OneOrMany`/`SeparatedList` (`Parser.cs:627-700`) при `LA(1) ∉ First(тело)` — в `_expected` записать `First(тело) ∪ exit-токены` (First/Follow уже посчитаны, `FollowSetCalculator.cs`). **always-on vs recovery-only:** п.1 (префильтр в `ParseAlternative`) — always-on; на корректном коде почти ничего не пропускает (правильная альтернатива первой), но это затрагивает принцип 5 (нулевая цена) → **обязательна перф-ассерт «чистый корпус, время парса в пределах шума»** | `Parser.cs`, `FollowSetCalculator.cs` | Финальный «expecting {...}» точен на границах циклов; snapshot-шум снижен (D2); перф-ассерт на чистом корпусе |
| **A5-1** (+уточнение §8.1) | **Ограниченный спекулятивный зонд** (аналог reset point / IsPossible* Roslyn): (a) S2 T2 `CanStart` выводится **автоматически** из кадров снимка (механика — `Speculative`, `Parser.Recovery.cs:360-377` + specCache B2); (b) T1-якоря тоже для Ref-кадров («каждая граница правила — точка resync»). **Семантика приёма:** приём = **потреблено ≥K токенов** (K=2 по умолчанию, настраиваемо — `RecoveryOptions.SoftDepth`); «лимит глубины» — только **потолок** работы, не условие приёма (для `ClassMember` до 2-го токена нужно ≥4 кадра `ClassMember→Field→Type→PredefinedType`, Cs1:108/148/549/556 — «≤3 кадра + failure на лимите» зонд бесполезен для member/statement). **Ограничение множества предикатов:** только Ref-ы тел циклов (`DeriveLoopAnchors`, 301-318) + верхний кадр, **не «любой Ref»** (иначе #предикатов×#позиций сжигает бюджет); **кап коллекции T2** — первые K позиций на предикат (сортировка по Pos → детерминизм); **исключить предикаты с чистым regex-First (`Identifier`)** — их покрывают S3/anchor-First (A5-7); **TDOPP/ContextScope-кадры — не выводить** (выражения стартуют почти чем угодно → ложные кандидаты; зеркало фолбэка A5-6). **Порядок (детерминизм, DoD 4):** авторские якоря первыми, затем по близости кадра top→bottom (конвенция `NearestOptions`, `RecoveryEngine.cs:335-349`). **Зависимость:** A5-1 требует A2 (собственный под-бюджет S2, волна 2) + B2 + B3 + A5-6 | `Recovery/RecoveryEngine.cs` (111-138), `DeriveLoopAnchors` (301-318) → обобщение | T2/T1 перестают быть только авторскими; E раньше; меньше попыток на «грязном» регионе (D1.6); **D1.7 (двойное повреждение) — приёмочный тест**; S3 не заглушен (регресс) |
| **A5-6** | **Per-call-site FOLLOW для recovery** (решение §9): в `GetTerminators` (стоп-набор S3) и `Follow(top)` (источник S1) — per-call-site значение по стеку снимка: `follow_site(i) = First(хвост Seq родителя после элемента-вызова) ∪ (nullable ? follow_site(i-1) : ∅)`, база {EOF}; O(глубины стека). **Граница:** Seq/Loop-кадры — per-site; **TDOPP-кадры** (`PostfixFrameLocation`, `Parser.cs:441`) и **ContextScope** — rule-level фолбэк (осознанно: продолжение операнда зависит от runtime `minPrecedence`, `Parser.cs:360`; циклолокальный recovery TDOPP покрывают `RecoveryPrefix/Postfix` + сброс точки, `Parser.cs:397-402`); нерезолвлённое место — rule-level. **S6 (A5-7) потребляет `GetTerminators` как стабильный интерфейс** — A5-6 меняет внутренности без переделки S6; **авторский `Options.Terminators` по-прежнему переопределяет per-site значение на своём кадре** (`Options.Terminators ?? follow_site(i)`, как сейчас `FollowSetCalculator.cs:239`; + §3.9 `Recoverable:false` → follow) — иначе override молча ломается. Глобальный fixed-point не меняется | `FollowSetCalculator.cs` (новая функция + `GetTerminators` 230-247) | Регресс-тест: `List := Ref(X) ',' Ref(X)` / `Block := Ref(X) '}'` — в контексте List ложная остановка на `'}'` не происходит; TDOPP-кейсы (выражения) — поведение не хуже текущего (корпус D1.5) |
| B2 (база) | **Кэш спекулятивных парсов на весь `Recover`**: поднять `specCache (rule, pos) → (Ok, EndPos)` из локального `GenerateS2` (`RecoveryEngine.cs:141`) в поле на время `Recover` (аналогично `matchCache` S3). Хранилище зонда A5-1 | `Recovery/RecoveryEngine.cs` | Хиты/миссы кэша в D2; время S2/A5-1 падает на файлах с многими ошибками |
| B3 (база) | **Дешёвые сканы S2/S3**: прыжки по токенам (trivia одним `Trivia.TryMatch`); для `Literal`-терминаторов — `input.IndexOf` вместо попозиционного `TryMatch` (DFA только для regex) | `Recovery/RecoveryEngine.cs` (107-108, 262, 397) | O(MaxSkip × #терминалов) → O(токенов × дешёвых проверок) |
| R2 (база) | **Стоп-предикат из ожидаемого множества**: S3/S6 останавливают скан на первой позиции, где совпадает ожидаемое продолжение (First-множества структур, ожидаемых вышестоящим правилом в E, ∪ терминаторы) | `Recovery/RecoveryEngine.cs`, `FollowSetCalculator.cs` | Дистанция пропуска короче; меньше ложных синхронизаций (D1.6) |

#### 5a.1 (B2) — декомпозиция на подзадачи

**Решения по микро-развилкам (закрыты до нарезки):**

1. **Тип поля** — обёртка `SpeculativeCache` (отдельный файл `ExtensibleParser/Recovery/SpeculativeCache.cs`). Обёртка держит счётчики вместе с состоянием кэша → переключение `GenerateS2` остаётся одним изменением поведения; голый `Dictionary` разорвал бы счётчики по `Parser` + `RecoveryEngine` (нарушение гранулярности).
2. **Точка сброса** — начало `Recover` (рядом с `HygieneRemovals = 0;`), `_specCache.Reset()` (кэш + счётчики атомарно). Время жизни кэша = один `Recover`.
3. **Видимость** — `private readonly SpeculativeCache _specCache` на `Parser` + `public SpecCache`-аксессор; `RecoveryEngine` (static) читает через `parser.SpecCache`. `#if RECOVERY` закрывает обе стороны.
4. **Счётчики (D2)** — инкремент внутри `SpeculativeCache.Speculative` (хит = `compute` не звали; промах = звали); на `Parser` — `public int SpecCacheHits => _specCache.Hits;` / `SpecCacheMisses` (read-only), сброс в `Recover`.
5. **Изоляция** — `specCache` не участвует в `PatchLog`/`HygieneCore`/`RollbackPatches`/`PatchMemo` (read-only кэш результатов спекулятивного парса на `scratch`-копии); единственный сброс — `Recover`.

**Non-goals (все подзадачи):** не менять `CreateScratchParser`/`ParseRuleOnce`/precedence зонда (=0); не менять `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`; не трогать S1–S6, кроме точки чтения `specCache`; не менять сигнатуру `RecoveryEngine.Generate`; не трогать `.csproj`; не коммитить.

**Общий Stop-if:** новый failed-тест не по подзадаче — стоп; поле `_specCache` уже существует — стоп и доклад; правка требует второго продакшн-файла — стоп, разбить; требуется решение уровня A2/A3 — стоп и доклад.

### 5a.1.1: обёртка `SpeculativeCache`
Файлы: `ExtensibleParser/Recovery/SpeculativeCache.cs` (новый); `Tests/ParserTests/Recovery/SpeculativeCacheTests.cs`
Цель: Создать `sealed class SpeculativeCache` — кэш `(rule,pos)→(Ok,EndPos)` + счётчики + `Reset`.
ДО: `specCache` — локальный `Dictionary` внутри `GenerateS2` (`RecoveryEngine.cs:155`); счётчиков нет.
ПОСЛЕ: `SpeculativeCache` с `Dictionary<(string,int),(bool,int)>`, `int Hits`, `int Misses`, методом `(bool,int) Speculative(string rule, int pos, Func<(bool,int)> compute)` (хит = `compute` не звали; промах = звали и записали) и `void Reset()` (чистит кэш + счётчики).
Тест: `SpeculativeCacheTests` — 1-й вызов `Speculative("r",0,()=>(true,10))` → `(true,10)`, `Misses==1`; 2-й вызов с другим `compute` → всё ещё `(true,10)`, `Hits==1`, `Misses==1`; `Reset()` → `Hits==0`, `Misses==0`.
Stop-if: тип `SpeculativeCache` уже существует; правка требует правки `Parser`/`RecoveryEngine`.
НЕ делать: не трогать `Parser`, `RecoveryEngine`, `GenerateS2`, `CreateScratchParser`, `ParseRuleOnce`; не менять сигнатуру `Generate`; не трогать `.csproj`; не коммитить.
Зависит от: нет.

### 5a.1.2: поле `_specCache` на `Parser` + сброс + аксессор + счётчики
Файлы: `ExtensibleParser/Parser.Recovery.cs`; `Tests/ParserTests/Recovery/SpecCacheFieldTests.cs`
Цель: Поднять кэш в поле на `Parser` со временем жизни один `Recover`, вынести публичные счётчики.
ДО: поля `_specCache` нет; `Recover` не сбрасывает кэш; публичных счётчиков нет.
ПОСЛЕ: `private readonly SpeculativeCache _specCache = new();` + `public SpeculativeCache SpecCache => _specCache;` + `public int SpecCacheHits => _specCache.Hits;` + `public int SpecCacheMisses => _specCache.Misses;` + в начале `Recover` (рядом с `HygieneRemovals = 0;`) — `_specCache.Reset();`.
Почему одна подзадача: поле без сброса протекает между Parse-вызовами; сброс без поля невозможен; аксессор и счётчики нужны для 5a.1.3 — по отдельности правки не имеют смысла.
Тест: `SpecCacheFieldTests` — (1) создать `Parser`; (2) `parser.SpecCache.Speculative("x", 0, () => (true, 1))` через публичный аксессор → `SpecCacheMisses == 1`; (3) повторный вызов с тем же ключом (другой `compute`) → `SpecCacheHits == 1`, `SpecCacheMisses == 1`; (4) `parser.Parse(...)` на любом входе (даже чистом) — вызовет `Recover` и сбросит кэш; (5) `SpecCacheHits == 0` и `SpecCacheMisses == 0`. (Проверка, что `GenerateS2` пишет в общий кэш, — в 5a.1.3, не здесь.)
Stop-if: поле `_specCache` уже существует; правка требует второго продакшн-файла; новый failed-тест не по подзадаче.
НЕ делать: не менять `GenerateS2`/`Speculative`-локалку в engine, `CreateScratchParser`, `ParseRuleOnce`, `HygieneCore`, `ApplyPatches`, `RollbackPatches`, `PatchMemo`; не менять сигнатуру `Generate`; не трогать `.csproj`; не коммитить.
Зависит от: 5a.1.1.

### 5a.1.3: переключение `GenerateS2` на `parser.SpecCache`
Файлы: `ExtensibleParser/Recovery/RecoveryEngine.cs`; `Tests/ParserTests/Recovery/SpecCacheSharedTests.cs`
Цель: `GenerateS2` читает/пишет общий кэш `Parser`, а не локальный.
ДО: `GenerateS2` создаёт локальный `specCache` (:155); `Speculative`-локалка (:157-165) читает/пишет его; локальный кэш умирает с вызовом.
ПОСЛЕ: локальный `specCache` удалён; `Speculative`-локалка = `parser.SpecCache.Speculative(ruleName, pos, () => { var r = scratch.ParseRuleOnce(ruleName, 0, pos, input); var ok = r.TryGetSuccess(out _, out var end); return (ok, ok ? end : -1); });`. `scratch` остаётся локальным (как было).
Тест: `SpecCacheSharedTests` — грамматика `TierBudgetTests` (`Module := '{' ZeroOrMany(Stmt) '}'`, `Stmt := Ident ':' Expr ';'`, `Expr` — TDOPP с шестью операторами), вход `"{ a: 1+ ### b ; }"` (Ident `b` после мусора — иначе `First(Stmt)` не совпадает со сканом и `Speculative` не вызывается, выигрывает S3); после `Parse` `parser.SpecCacheMisses > 0` (до правки локальный кэш не инкрементит обёртку → 0 → тест падает; после → проходит).
Stop-if: правка требует второго продакшн-файла; изменилась сигнатура `Generate`; новый failed-тест не по подзадаче (регресс S1–S6).
НЕ делать: не менять `CreateScratchParser`, `ParseRuleOnce`, precedence зонда (=0), `HygieneCore`, `ApplyPatches`, `RollbackPatches`, `PatchMemo`, S1–S6 кроме точки чтения; не трогать `.csproj`; не коммитить.
Зависит от: 5a.1.2.

**Порядок:** обёртка (тип) → поле/сброс/счётчики (инфраструктура на `Parser`) → переключение (тест опирается на счётчики из 5a.1.2). Планка: 348/0/2 + ровно новые тесты подзадачи. Прогресс — `docs/RecoveryImprovementPlan-progress5a.1.md`.

#### 5a.2 (B3) — декомпозиция на подзадачи

**Решения по вопросам (закрыты до нарезки):**

1. **IndexOf в S3 vs счётчик вложенности** — (в): IndexOf в S3 не делаем, только trivia jump. Терминаторы S3 однобуквенные (`TryMatch` уже ~`input[s]==c`); счётчик `{ } ( ) [ ]` требует попозиционного обхода — IndexOf его сломает.
2. **Где IndexOf помогает** — только в S2. First-множества S2 содержат multi-char `Literal` (ключевые слова) — `TryMatch` делает полное сравнение, `input.IndexOf` — быстрый скан. В S3 однобуквенные — выигрыша нет.
3. **Trivia jump — как именно** — прыжок одним `Trivia.TryMatch` в начале каждой итерации цикла (в обоих сканах). Trivia-бег не содержит терминаторов/First-совпадений → пропуск безопасен.
4. **S3 и комментарии (решение (б))** — прыгать по всему trivia (включая `//` и `/* */`). Скобки внутри `/* ... */` сейчас ложно учитываются в `curly/paren/bracket` — это баг (парсер сам их игнорирует); jump = исправление. Обязательный тест: `}` внутри `/* ... */` → S3 не останавливается на нём.
5. **Ассерт счётчика (без хардкода N)** — два входа: с trivia-паддингом и без; ассерт `counter(с паддингом) - counter(без) < длина_паддинга`. Не требует внутренних `e`/`maxS`.

**Non-goals (все подзадачи):** не менять `GenerateS6`/`GenerateS1`/`GenerateS4`/`GenerateS5`; не менять семантику `SpeculativeCache`; не менять `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`; не менять сигнатуру `RecoveryEngine.Generate`; не трогать `.csproj`; не коммитить.

**Общий Stop-if:** новый failed-тест не по подзадаче — стоп; правка требует второго продакшн-файла — стоп, разбить; нужно решение уровня A2/A3 — стоп и доклад; тестовый вход не попадает в целевой скан — стоп и доклад.

### 5a.2.1: счётчики сканированных позиций (инфраструктура)
Файлы: `ExtensibleParser/Parser.Recovery.cs`
Цель: добавить `S2ScanPositions`/`S3ScanPositions` на `Parser` (сброс в `Recover`), по образцу `HygieneRemovals`.
ДО: счётчиков нет.
ПОСЛЕ: `public int S2ScanPositions { get; private set; }` + `public int S3ScanPositions { get; private set; }`; в блоке сброса `Recover` (рядом с `HygieneRemovals = 0;`) — `S2ScanPositions = 0; S3ScanPositions = 0;`. Инкремент — НЕ здесь (в 5a.2.2/5a.2.3).
Тест: нет отдельного (инфраструктура); счётчики проверяются в 5a.2.2/5a.2.3, где есть инкремент.
Стоп-если: свойство уже существует; правка требует второго продакшн-файла.
Не делать: не трогать `RecoveryEngine`, S1–S6, `SpeculativeCache`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, сигнатуру `Generate`, `.csproj`; не коммитить.
Зависит от: нет.

### 5a.2.1a: Note-методы для счётчиков
Файлы: `ExtensibleParser/Parser.Recovery.cs`
Цель: добавить публичные Note-методы, чтобы движок (из `GenerateS2`/`GenerateS3`) мог инкрементить счётчики (у них `private set`).
ДО: счётчики `S2ScanPositions`/`S3ScanPositions` имеют `private set` — движок не может их инкрементить.
ПОСЛЕ: рядом со счётчиками — `public void NoteS2ScanPosition() => S2ScanPositions++;` и `public void NoteS3ScanPosition() => S3ScanPositions++;`.
Тест: нет отдельного (инфраструктура); счётчики проверяются в 5a.2.2/5a.2.3.
Стоп-если: метод уже существует; правка требует второго продакшн-файла.
Не делать: не трогать `RecoveryEngine`, S1–S6, `SpeculativeCache`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, сигнатуру `Generate`, `.csproj`; не коммитить.
Зависит от: 5a.2.1.

### 5a.2.2: S2 — счётчик + trivia jump
Файлы: `ExtensibleParser/Recovery/RecoveryEngine.cs`; `Tests/ParserTests/Recovery/S2TriviaJumpTests.cs`
Цель: в S2-скане инкрементить `parser.S2ScanPositions` на каждой реально проверенной позиции и пропускать trivia-бег одним `Trivia.TryMatch`.
ДО: цикл `for (var s = e; s <= maxS && !foundT1; s++)` (:281) идёт по каждой позиции; счётчика нет.
ПОСЛЕ: в начале тела цикла `parser.NoteS2ScanPosition()`; после инкремента — если `parser.Trivia.TryMatch(input, s)` даёт длину `k>0`, то `s += k` (прыжок на конец бегa) и `continue`. Логика `FirstMatchesAt`/`Speculative`/`AddResyncCandidate` не меняется.
Почему одна подзадача: счётчик и trivia-jump неразрывны — без счётчика jump не принять (тест «тот же результат» проходит и до, и после); счётчик измеряет именно то, что jump уменьшает.
Тест: `S2TriviaJumpTests` — грамматика `TierBudgetTests`; два входа: `"{ a: 1+ ### b ; }"` (без паддинга) и `"{ a: 1+   ### b ; }"` (с trivia-паддингом 3 пробела). Ассерт: (а) `S2ScanPositions > 0` (скан доходит до `Speculative` на `b`); (б) resync-позиция/диагностика те же, что до правки; (в) `S2ScanPositions(с паддингом) - S2ScanPositions(без) < 3` (бег пропущен). Падает без правки: паддинг не пропускается → разница == 3 (не < 3).
Стоп-если: вход не доходит до S2-скана (`S2ScanPositions==0` после правки); resync-позиция изменилась; новый failed-тест не по подзадаче; правка требует второго файла.
Не делать: не трогать S1/S3/S4/S5/S6, `GenerateS2`-подпись, `SpeculativeCache`, `CreateScratchParser`/`ParseRuleOnce`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, сигнатуру `Generate`, `.csproj`; не коммитить.
Зависит от: 5a.2.1, 5a.2.1a.

### 5a.2.3: S3 — счётчик + trivia jump (решение (б): по всему trivia)
Файлы: `ExtensibleParser/Recovery/RecoveryEngine.cs`; `Tests/ParserTests/Recovery/S3TriviaJumpTests.cs`
Цель: в S3-скане инкрементить `parser.S3ScanPositions` на каждой реально проверенной позиции и пропускать trivia-бег (включая комментарии) одним `Trivia.TryMatch`.
ДО: цикл `for (var s = e + 1; s <= maxS; s++)` (:417) идёт по каждой позиции, обновляя `curly/paren/bracket` по `input[s-1]`; счётчика нет.
ПОСЛЕ: в начале тела цикла `parser.NoteS3ScanPosition()`; если `parser.Trivia.TryMatch(input, s)` даёт `k>0` — `s += k` и `continue`. Решено (б): прыжок по всему trivia (включая `//` и `/* */`) — скобки внутри `/* ... */` больше не учитываются в `curly/paren/bracket` (исправление: парсер сам их игнорирует). Логика `Match`/глубины не меняется.
Почему одна подзадача: как в 5a.2.2 — счётчик и jump неразрывны.
Тест: `S3TriviaJumpTests` — грамматика `TierBudgetTests`; два входа: `"{ a: 1+ ### ; }"` (без паддинга) и `"{ a: 1+   ### ; }"` (с паддингом 3 пробела). Ассерт: (а) `S3ScanPositions > 0`; (б) S3-диагностика `skip to terminator` та же, что до правки; (в) `S3ScanPositions(с паддингом) - S3ScanPositions(без) < 3`. **Обязательный (решение (б)):** вход с `}` внутри `/* ... */` (напр. `"{ a: 1+ /* } */ ### ; }"`) — ассерт, что S3 НЕ остановился на `}` внутри комментария (resync-позиция совпадает с вариантом без комментария). Падает без правки: `S3ScanPositions==0`; тест (б) падает без правки, т.к. сейчас `}` в `/* */` ложно учитывается в счётчике.
Стоп-если: вход не доходит до S3 (`S3ScanPositions==0` после правки); S3-диагностика изменилась; новый failed-тест не по подзадаче; правка требует второго файла.
Не делать: не трогать S1/S2/S4/S5/S6, `SpeculativeCache`, `CreateScratchParser`/`ParseRuleOnce`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, сигнатуру `Generate`, `.csproj`; не коммитить.
Зависит от: 5a.2.1, 5a.2.1a.

### 5a.2.4: S2 — прыжок к ближайшему multi-char Literal (IndexOf)
Файлы: `ExtensibleParser/Recovery/RecoveryEngine.cs`; `Tests/ParserTests/Recovery/S2IndexOfTests.cs`
Цель: `GenerateS2` — прыжок к ближайшему multi-char `Literal` из First-множеств якорей вместо пошагового `FirstMatchesAt` на каждой позиции.
ДО: `FirstMatchesAt` (:164) на каждой позиции зовёт `t.TryMatch(input, pos)` для каждого терминала First (для multi-char `Literal` — полное строковое сравнение).
ПОСЛЕ: (1) предвычислить множество multi-char `Literal`-строк из First-множеств всех якорей (однократно, до цикла); (2) в цикле `next = min(input.IndexOf(lit, s) для lit из множества, где IndexOf >= 0)`; если ни один не найден — `break`; (3) если `next > s` — `s = next` (прыжок к вхождению); (4) однобуквенные `Literal` и regex — как были, обычный пошаговый проход. `Speculative`/`AddResyncCandidate` не меняются.
Пометка: переписывание цикла S2; объём больше, чем у 5a.2.2, но метод один — `GenerateS2`, файл один.
Тест: `S2IndexOfTests` — тот же вход, что в 5a.2.2 (`"{ a: 1+ ### b ; }"` + паддинг). Ассерт: resync-позиция та же, что до правки; `S2ScanPositions` меньше, чем после 5a.2.2 (IndexOf пропустил позиции).
Стоп-если: переписывание требует правки вне `GenerateS2` — стоп и доклад, не импровизировать; resync-позиция изменилась; вход не доходит до S2; новый failed-тест не по подзадаче; правка требует второго файла.
Не делать: не трогать S1/S3/S4/S5/S6, `SpeculativeCache`, `CreateScratchParser`/`ParseRuleOnce`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, сигнатуру `Generate`, `.csproj`; не коммитить.
Зависит от: 5a.2.2.

**Порядок:** счётчики (инфраструктура) → Note-методы (5a.2.1a) → S2-счётчик+trivia → S3-счётчик+trivia → S2-IndexOf (опирается на trivia-базу из 5a.2.2). S2-trivia и S3-trivia независимы (разные сканы). Планка: 351/0/2 + ровно новые тесты подзадачи. Прогресс — `docs/RecoveryImprovementPlan-progress5a.2.md`.

**Критерий волны:** на D1-корпусе: меньше итераций и попыток на точку (D2), «expecting»
сообщения точнее (ручная проверка по сценариям), TDOPP-регрессии отсутствуют.

---

### Волна 6 — диагностика и метрики

| # | Что | Где | Приёмка |
|---|---|---|---|
| **A5-2** | **Диагностика, выводимая из дерева** (единый источник правды): `_recoveryDiagnostics` (`Parser.Recovery.cs:21-22`) — кэш; добавить `DeriveDiagnostics(root)` — обход IsRecovery-узлов (`SyntaxTree.cs:149-170`): нулевая вставка → `Inserted`, абсорбер → `Skipped` с текстом узла, `Unrecovered` — из результата (A4-2). Публичный список — производный от дерева | `Parser.Recovery.cs`, `SyntaxTree.cs` | Тест: список == обход дерева после откатов/итераций; запрос диагностики поддерева работает (IDE-сценарий) |
| D2 (база, полная) | Счётчики по стратегиям: accept/rollback по каждой S (S0..S6); удаления hygiene до/после B1; хиты/миссы specCache (B2); время по фазам (основной парс / генерация / re-парсы) | `Parser.Recovery.cs`, `RecoveryEngine.cs` | «Тормозной сценарий» виден в метриках, а не в таймауте |
| **A4-6** | **Канал качества грамматики** (аналог `DiagnosticErrorListener` ANTLR4): публичный `GrammarDiagnostics` (отдельно от `RecoveryDiagnostics` — те про ошибки ввода): якорь использован N раз / никогда; все recovery-точки правила — одна и та же; `Recoverable:false` регион никогда не срабатывает на корпусе; S2-якорь избыточен (всегда один и тот же) | новый тип + сбор в D2 | Автор грамматики (C#, будущие языки) получает обратную связь по разметке |

**Критерий волны:** расхождения список/дерево невозможны (тест); отчёт по качеству
грамматики генерируется на D1-корпусе.

---

### Волна 7 — полировка

| # | Что | Где | Приёмка |
|---|---|---|---|
| R3 (база) | **Калибровка depth-guard**: перекалибровка `_maxParseDepth` (`Parser.Recovery.cs:152-156`, сейчас `input.Length*4+128`) под реальный бюджет стека (эмпирика: 350 уровней вложенности = падение); превентивная проверка в горячих точках (аналог `EnsureSufficientExecutionStack`); результат сработавшего guard'а — дно-контракт A5-4 с диагностикой `InsufficientStack` | `Parser.Recovery.cs`, `Parser.cs` | Репро 3.0a + 350-уровневый кейс проходят; процесс не погибает |
| **A5-3** / R5 (база) | **Диагностика на первом слове грязного региона**: span абсорбера `[E..S)` — не весь спан, а первое небелое «слово» внутри (превью есть — `RecoveryEngine.cs:712`); missing-узел с пропущенным текстом → диагностика на первом пропущенном токене; «конец предыдущей строки» для missing на новой строке (R5) | `RecoveryDiagnostic`, `RecoveryEngine.cs` | Короткий squiggle на первом «виновном» слове (IDE-визуальная проверка) |
| B5 (база) | **Кластеризация дорогих точек**: после точки, где потрачено много попыток/времени (порог из профиля), следующие итерации в том же «грязном» регионе стартуют сразу с грубого тира (S3/S6), пропуская S2 | `Recover`, `RecoveryProfile` | Д1.6 (ошибки каждые N строк) — время растёт линейно, не квадратично |
| **A5-5** | **SoftSeparator для `SeparatedList`** (аналог `allowSemicolonAsSeparator` Roslyn): опция `SoftSeparator(t)` — чужой сепаратор на позиции сепаратора потребляется с диагностикой `Skipped` и трактуется как нужный. Декларативно (grammar-driven), не лямбдой | `Rules.cs` (530-558), `Parser.cs` (`ParseSeparatedList`) | Класс «не тот сепаратор» (`;`/`.`/`:`) — инлайн (S0), одна диагностика, без S2/S3 |
| **A4-7** | **Точный expected в финальном сообщении**: `FatalError`/`Unrecovered` — expected = `Expected` верхнего кадра ∪ `GetTerminators(стек)` (per-site после A5-6) ∪ First суффиксных обязательств (механика S2/S4, `RecoveryEngine.cs:215-239` — вынести в общую функцию) | `Parser.Recovery.cs` (`FinalizeResult` 255-257) | «expecting {...}» отражает весь контекст, а не один упавший терминал |
| **A5-8** (опционально) | **K-кандидатов в S3**: не `break` на первом стоп-пункте (`RecoveryEngine.cs:444-445`), а K ближайших (3–5) как отдельные кандидаты. После A5-6 стоп-набор точен и один кандидат звучен — это защита в глубину; можно не делать | `Recovery/RecoveryEngine.cs` | (если делать) ложная первая остановка не убивает итерацию |
| E (база) | **Структурно под кодогенерацию**: держать `RecoveryEngine.Generate` чистым; `Injection` + `MemoPatch` — единственные единицы патча (не усложнять); задокументировать свойство «re-проход префикса дёшевый» (memo-проверка до `PushRuleFrame`, `Parser.cs:220-230`) — сохранять при рефакторинге; не вводить глобальный чарт | — | Рефакторинг не ломает инварианты (тесты + ревью) |

---

## Сводка: новые пункты → волны

| # | Пункт | Источник | Волна | Приоритет |
|---|---|---|---|---|
| A5-7 | S6 = паник-дно (уточнение A1) | ANTLR4 `recover`/`consumeUntil` | 1 | высокий |
| A4-2 | «отчёт и продолжить»: список ошибок, `Unrecovered` | ANTLR4 catch-продолжение | 1 | высокий |
| A5-4 | дно-контракт результата (`Success<T>` в IDE) | Roslyn `CreateForGlobalFailure` | 1 | высокий |
| A4-5 | runtime-стратегия (bail) вместо `#if RECOVERY` | ANTLR4 `BailErrorStrategy` | 4 | высокий |
| A5-6 | per-call-site FOLLOW (граница TDOPP/ContextScope) | ANTLR4 `getErrorRecoverySet` | 5 | средний |
| A5-1 | спекулятивный зонд + T1-якоря для любого Ref-кадра | Roslyn reset point; ANTLR4 sync-точки | 5 | средний |
| A4-1 | тихая зона (side-эффекты только дальше E) | ANTLR4 `errorRecoveryMode` | 5 | средний |
| A4-3 | single-token deletion, ранг 1 | ANTLR4 `singleTokenDeletion` | 5 | средний |
| A4-4 | First-префильтр + ожидаемые на границе циклов | ANTLR4 `sync` | 5 | средний |
| A5-2 | диагностика, выводимая из дерева | Roslyn node-attached diagnostics | 6 | средний |
| A4-6 | канал качества грамматики | ANTLR4 `DiagnosticErrorListener` | 6 | низкий |
| A5-3 | диагностика на первом слове региона | Roslyn `GetDiagnosticSpanForMissingNodeOrToken` | 7 | низкий |
| A5-5 | SoftSeparator | Roslyn `allowSemicolonAsSeparator` | 7 | низкий |
| A4-7 | точный expected в финальном сообщении | ANTLR4 `ATN.getExpectedTokens` | 7 | низкий |
| A5-8 | K-кандидатов в S3 | — (защита в глубину) | 7 / опционально | низкий |

Пункты базы (A1–A3, B1–B5, C1–C2, D1–D2, E, R1–R6) распределены по волнам выше по их
соответствующим волнам; рекомендуемый порядок базы из `RecoveryImprovementProposal.md`
(1 → 2 → 6 → 3 → 4 → 5 → 7) сохранён, волна 0 (D1/D2-ядро) вынесена вперёд.

## Итоговые критерии принятия (Definition of Done плана)

1. **100% до EOF (IDE-профиль):** на любом входе — `Success<T>` с деревом, покрывающим
   весь ввод (I4); список диагностик `Inserted/Skipped/Unrecovered`; `FatalError` —
   только в Compiler-профиле.
2. **Все ошибки файла** отчитаны за один проход (D1.1, D1.6).
3. **Work-bound:** худшее время ограничено профилем (лимиты итераций/попыток/MaxSkip +
   time-budget с деградацией до дна, не до `FatalError`).
4. **Детерминизм:** порядок кандидатов фиксирован (`(Rank, Cost, Pos, RuleName,
   TerminalKind)`); повторный запуск — идентичный результат.
5. **I6:** на корректном коде recovery-работы нет (замер D1-базовый).
6. **Регрессии:** ParserTests + CSharpGrammarTests зелёные; TDOPP-сценарии (выражения,
   приоритеты, ассоциативность) — без изменений после A5-6.
7. **Наблюдаемость:** медленный сценарий виден в метриках (D2), а не в таймауте.

## Риски

| Риск | Митигция |
|---|---|
| A5-6: граница TDOPP нарушит текущее поведение выражений | rule-level фолбэк обязателен; регресс-тесты D1.5 до и после; per-site только для Seq/Loop-кадров |
| B1: точная hygiene — тонкая семантика инвалидации | тест «отравленного memo» (failure, дотянувшийся до E, удалён; локальный — сохранён); метрика числа удалений |
| A5-7: S6 без snapshot — синтетический кадр и взаимодействие со строгими регионами | C1: дно не «закрывает» `Recoverable:false` регион; контракт-тесты |
| A4-5: runtime-bail повторит дефект no-recovery (пропавший depth-guard) | guard в общем коде, активен во всех режимах; тест R3-репро в bail-режиме |
| B4: деградация time-budget может «потерять» дерево | лестница деградации всегда заканчивается дном S6 (полное дерево + диагностика) |
| A5-1: щедрый T2 (множество предикатов / чистый `Identifier`) сжигает тир-бюджет, S3/S6 не пробуются (ранг 2 < 3, бюджет 3/точку, `Parser.Recovery.cs:39`) | собственный под-бюджет S2 (A2) + кап коллекции T2 + исключение чистых regex-First; регресс-тест «мусор с identifier-токенами» → S3 не заглушен; D1.7 |
| Объём волн 1–2 | волна 0 (корпус) ставится первой — откат/замедление видно по замерам сразу |
