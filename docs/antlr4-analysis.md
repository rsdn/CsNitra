# ANTLR4 error recovery — анализ и предложения для ExtensibleParser

Объекты анализа:

- ANTLR4 (Java reference runtime): `C:\RSDN\antlr4\runtime\Java\src\org\antlr\v4\runtime\...`,
  C# runtime: `C:\RSDN\antlr4\runtime\CSharp\src\...` (1:1 порт Java).
- CsNitra: `ExtensibleParser\Parser.cs`, `Parser.Recovery.cs`, `Parser.NoRecovery.cs`,
  `Recovery\RecoveryEngine.cs`, `FollowSetCalculator.cs`.

Связанные документы: `RecoveryMethods.md` (состояние CsNitra), `RecoveryImprovementProposal.md`
(предложения A–F из анализа Roslyn/Nitra). Раздел 3 настоящего документа — **только идеи,
не покрытые** A–F; совпадающие места ссылаются на соответствующий пункт.

---

## 1. Как устроено восстановление в ANTLR4

### 1.1. Архитектура: разделение ответственности

Три независимых слоя, каждый заменяем:

| Слой | Роль | Реализация |
|---|---|---|
| Simulator (`ParserATNSimulator`) | «что произошло»: предсказание, бросает `NoViableAltException` / `InputMismatchException` | `atn\ParserATNSimulator.java` |
| Strategy (`ANTLRErrorStrategy`) | «что делать с ошибкой»: отчёт, inline-чинка, ресинхронизация | `misc\ANTLRErrorStrategy.java:28-120`, `misc\DefaultErrorStrategy.java` |
| Listener (`ANTLRErrorListener`) | «как сообщить»: консоль, IDE, диагностика | `ConsoleErrorListener`, `DiagnosticErrorListener` |

Интерфейс strategy — 6 методов: `reset`, `recoverInline`, `recover`, `sync`,
`inErrorRecoveryMode`, `reportError`, `reportMatch` (`ANTLRErrorStrategy.java:28-120`).
Парсер не знает деталей: `_errHandler` — поле (`Parser.java:101`), подменяется целиком.

Три точки входа ошибок:

1. `Parser.match(ttype)` — mismatch одиночного токена → `_errHandler.recoverInline(this)`
   (`Parser.java:198-216`);
2. match-множество альтернативы — токен вне множества → `recoverInline` (шаблон `CommonSetStuff`,
   `tool\resources\org\antlr\v4\tool\templates\codegen\Java\Java.stg:694-706`);
3. предсказание — `adaptivePredict` бросает `NoViableAltException`, его ловит catch правила.

**Каркас каждого сгенерированного правила** (`Java.stg:431-465`):

```csharp
try { ... тело ... }
catch (RecognitionException re) {
    _localctx.exception = re;            // привязка ошибки к узлу дерева
    _errHandler.reportError(this, re);  // отчёт (или подавление)
    _errHandler.recover(this, re);      // ресинхронизация
}
finally { exitRule(); }                 // контекст ЗАВЕРШЁН всегда
```

`enterRule`/`enterRecursionRule` (`Parser.java:625-631, 684-692`) исключения не ловят —
ловля в catch тела правила. Следствие: правило гарантированно завершается, стек контекстов
не повреждается, следующая ошибка обрабатывается в чистом состоянии.

Особый режим — `BailErrorStrategy` (`misc\BailErrorStrategy.java:39-72`): `recover`/`recoverInline`
не чинят, а оборачивают исходную ошибку в `ParseCancellationException` и проваливают парсинг;
`sync` — пустой. Назначение — fail-fast первый проход (two-stage parsing, §1.4).

### 1.2. Ключевые алгоритмы (все в `misc\DefaultErrorStrategy.java`)

**`sync()` — синхронизация на границах (230-286).** Вызывается сгенерированным кодом **перед
каждым decision-состоянием**: перед блоком альтернатив, перед опционалом, перед `*`/`+` и на
каждой итерации в loop-back состоянии. Логика:

1. в режиме recovery — ничего не делать (234-236);
2. дешёвая проверка: `LA(1) ∈ atn.nextTokens(state)` (кэшируемое FIRST внутри правила,
   `atn\ATN.java:95-100`) — всё ок (242-248);
3. по типу ATN-состояния (260-285):
   - начало блока/цикла → **single-token deletion** (266), иначе `InputMismatchException` →
     полный `recover()` через catch правила;
   - loop-back цикла → `reportUnwantedToken` + `consumeUntil(expecting ∪ recoverySet)` (275-279)
     — в цикле агрессивно остаёмся в цикле: съедаем до начала следующей итерации или до выхода.

**`recoverInline()` — локальная чинка на 2-lookahead (463-489).** Срабатывает при mismatch
одиночного токена/множества, **не выходя из правила**:

1. **SingleTokenDeletion** (`singleTokenDeletion`, 544-562): если `LA(2) ∈ expected` — токен
   `LA(1)` лишний: «extraneous input X expecting {...}», съедаем его, продолжаем (555-558);
2. **SingleTokenInsertion** (`singleTokenInsertion`, 508-523): если `LA(1) ∈ nextTokens(nextState, ctx)`
   (LL2-проверка: текущий токен законно следует **за** ожидаемым, 516) — токен пропущен:
   «missing X at Y», в дерево вставляется **выдуманный** токен `<missing X>` (476-477, 583-603);
3. иначе — `InputMismatchException` → эскалация в `recover()`.

**`recover()` — полная ресинхронизация (158-181).** После `NoViableAltException` и неспасённых
mismatch'ов:

- **failsafe от зацикливания** (164-175): если позиция не сдвинулась с последней ошибки и
  текущее ATN-состояние уже было в `lastErrorStates` — принудительно съедаем один токен
  (гарантия прогресса);
- `consumeUntil(getErrorRecoverySet())` (180).

**`getErrorRecoverySet()` — runtime context-sensitive FOLLOW (741-756).** Обходит **стек
вызовов** (`ctx.invokingState`) и объединяет `atn.nextTokens(followState)` каждого правила
цепочки (EPSILON вычитается, 753). Это не предвычисленный глобальный FOLLOW, а **runtime-объединение
по фактическому стеку** — точная sync-точка без накладных расходов в нормальном режиме.
Комментарий (649-740) приводит классический пример: в `a : '[' b ']' | '(' b ')' ; b : c '^' INT ; c : ID | INT`
на входе `[]` восстановление идёт до `{']','^'}` — FOLLOW всей цепочки, а не глобального
FOLLOW(c)={'^'}, который съел бы весь ввод до EOF.

**`consumeUntil()` (759-768)** — цикл до первого токена из множества (или EOF).

### 1.3. Как ANTLR4 гасит каскад ошибок («одна ошибка → один отчёт → продолжить»)

1. **Флаг `errorRecoveryMode`** (27; begin/end 71-93): пока не произошло ни одного успешного
   совпадения после ошибки — все `reportError` гасятся (130-133), `sync` не работает (234-236).
   Конец режима — `reportMatch` (101-103) при любом успешном `match` (`Parser.java:204`).
   Весь «хвост» мусора после одной ошибки проглатывается молча до первого валидного совпадения.
2. **Failsafe прогресса** — `lastErrorIndex` + `lastErrorStates` (§1.2): между двумя ошибками
   минимум один токен/состояние меняется; зацикливание исключено конструктивно.
3. **Error nodes в дереве** — в режиме recovery `Parser.consume()` добавляет проглоченный токен
   как `ErrorNodeImpl`, а не `TerminalNode` (`Parser.java:568-593, 609-611`;
   `tree\ErrorNodeImpl.java:11-26`). Дерево остаётся полным по вводу, а visitor/IDE отличают
   «реальные» узлы от проглоченных при ресинхронизации (подсветка, игнор при генерации).
4. **Ошибки на границах** — `sync()` обнаруживает несогласованность **до** входа в блок/итерацию,
   где recovery локален (delete/insert/consume до границы). Javadoc (183-228): без sync в
   `classDef : 'class' ID '{' member* '}'` лишний токен между членами заставлял проглатывать
   до следующего класса; с sync — только до следующего члена.
5. **Отложенный, но точный отчёт** — если в `sync` обнаружен EPSILON-вариант, запоминается
   `nextTokensContext`/`nextTokensState` (39-52, 250-258); при последующем
   `InputMismatchException` ожидаемые вычисляются по сохранённому **истинному** контексту
   (`ATN.getExpectedTokens`, `atn\ATN.java:166-195` — FIRST + подъём по `invokingState` с
   объединением FOLLOW, пока не исчез EPSILON, + EOF). Сообщение «expecting {...}» остаётся точным,
   даже если ошибка обнаружена позже точки, где она стала очевидной.
6. **Отложенный NoViableAlt** — `ParserATNSimulator.getSynValidOrSemInvalidAltThatFinishedDecisionEntryRule`
   (1283-1302): при достижении ERROR-состояния симулятор сначала проверяет, не довела ли
   какая-то альтернатива разбор до конца правила; если да — возвращает её и **откладывает**
   ошибку, чтобы та была локализована глубже.
7. **Лексер не останавливает поток** — `LexerNoViableAltException` → отчёт + пропуск **одного
   символа** + `SKIP` (`Lexer.java:112-165, 352-357, 404-409`); одна лексическая ошибка не
   порождает цепочку parser-ошибок.

### 1.4. Режимы предсказания и two-stage parsing

«Recovery modes» в ANTLR4 — на самом деле **режимы предсказания**
(`atn\PredictionMode.java:24-83`): `SLL` (быстрый, без внешнего контекста) и `LL` (полный
контекст, дефолт). Механика двухступенчатого предсказания — `ParserATNSimulator.adaptivePredict`
(313-381) → `execATN` (413-519) → при SLL-конфликте failover на `execATNWithFullContext`
(622-745).

Связь с recovery:

- точность предсказания напрямую определяет качество recovery: на неверном вводе **меньше
  ложных** `NoViableAltException` и ожидаемые множества точнее — меньше и грубее ресинхронизаций;
- `reportAttemptingFullContext` / `reportContextSensitivity` / `reportAmbiguity` (2135-2160) —
  **диагностический канал качества грамматики** (не recovery): `DiagnosticErrorListener`
  (`DiagnosticErrorListener.java:37-152`) сообщает автору грамматики, где решение
  неоднозначно/контекстно-зависимо — отдельный от ошибок пользователя поток.

**Two-stage parsing** (javadoc `ParserATNSimulator.java:201-248`): первый проход —
`PredictionMode.SLL` + `BailErrorStrategy` (быстрый, без recovery, fail-fast); при ошибке —
повторный проход с `LL` + `DefaultErrorStrategy`. Формальное обоснование: если SLL-конфликт
разрешается LL в singleton, SLL либо даст тот же ответ, либо ошибку — ошибка первого прохода
достоверна. Цена — второй проход только на битом вводе.

---

## 2. Сравнение с ExtensibleParser (CsNitra)

### 2.1. Архитектурное различие

| | ANTLR4 | CsNitra |
|---|---|---|
| Модель | **инлайн**: парсер продолжает с точки ошибки в том же проходе | **внешний итеративный**: полный re-парс от начала с патчами (`Recover`, `Parser.Recovery.cs:261-357`) |
| Обоснование | ошибка = локальное состояние ATN, синхронизируемое FOLLOW | при longest-match падение отдельного правила **не является ошибкой** — её достоверно сообщает только верхний уровень (`RecoveryMethods.md` §1) |
| Принятие чинки | локальная эвристика (2-lookahead), без проверки «помогло ли» | полный re-парс; кандидат принимается **только** при прогрессе E2 > E или Success@EOF (`Parser.Recovery.cs:321-334`) |
| Откат | нет (состояние мутируется) | `PatchLog` + `RollbackPatches` (`Parser.Recovery.cs:453-472`) |

Итог: механизмы ANTLR4 — **дешёвые и локальные**, но эвристические; кандидаты CsNitra —
**валидированные re-парсом**, но каждый итеративный проход дороже. Это не «кто лучше», а
разный баланс; заимствовать надо конкретные приёмы, а не модель.

### 2.2. Соответствие механизмов

| Механизм ANTLR4 | Эквивалент в CsNitra | Комментарий |
|---|---|---|
| `recoverInline`: single-token insertion (463-489, 508-523) | **S1** — вставка терминала (`Recovery/RecoveryEngine.cs:38-98`) + `OftenMissed` (инлайн, `Parser.Recovery.cs:208-211`) | CsNitra валидирует вставку re-парсом — точнее ANTLR4 (у того только LL2-проверка) |
| `recover` + `consumeUntil` (158-181, 759-768) | **S3** — panic-скан до терминатора (`RecoveryEngine.cs:364-511`) | паритет; у CsNitra скан с учётом вложенности `{}()/[]` (400-416) |
| `sync()` + loop-back (230-286) | **S2** — resync к known-good точке (104-298) + выводимые якоря циклов (301-318) | CsNitra **доказывает** валидность точки спекулятивным parse; ANTLR4 доверяет FOLLOW без проверки — CsNitra точнее, ANTLR4 дешевле |
| `getErrorRecoverySet` (741-756) | `GetTerminators(stack)` (`FollowSetCalculator.cs:230-247`) | прямой аналог: runtime-объединение FOLLOW по стеку снимка |
| `ATN.getExpectedTokens` (166-195) | `ExpectedFor` / суффиксные обязательства S2/S4 (`Parser.Recovery.cs:189`, `RecoveryEngine.cs:215-239`) | паритет для recovery; для финального сообщения — слабее (см. §2.3) |
| `errorRecoveryMode` + `reportMatch` (27, 101-133) | **нет** — см. §3, A4-1 |
| failsafe `lastErrorIndex/lastErrorStates` (164-178) | монотонность E (`Parser.Recovery.cs:286-288`) + zero-progress guard + лимиты 64×3 | паритет, у CsNitra предохранителей больше |
| ErrorNode в дереве (`Parser.java:568-593`) | `TerminalNode(IsRecovery)` / `IsAbsorber` (`SyntaxTree.cs:149-170`), `SeqNode.Elements` скрывает абсорберы (196-197) | паритет |
| отложенный точный отчёт (`nextTokensContext`, 250-258) | `FailureSnapshot` (стек, упавший терминал, expected — `Recovery/FailureSnapshot.cs`) | паритет для recovery; для финального `FatalError` — нет (см. §2.3, A4-7) |
| `BailErrorStrategy` (39-72) | `#if !RECOVERY` — `Parser.NoRecovery.cs` (компиляционный флаг) | у CsNitra режим **compile-time**, не runtime — см. §3, A4-5 |
| two-stage SLL+LL (201-248) | **нет** (см. §3, A4-5) |
| `DiagnosticErrorListener` (37-152) | счётчики `RecoveryPasses`/`EngineGenerateCalls` (`Parser.Recovery.cs:25-29`) | у CsNitra нет канала **для автора грамматики** — см. §3, A4-6 |
| лексер: проглотить 1 символ (352-357) | **нет** (покрыто R1 «мусорный терминал» в `RecoveryImprovementProposal.md`) |
| `reportLine` / `recoverLine` (ANTLR3) | — | в ANTLR4 уже упразднены, брать нечего |

### 2.3. Где CsNitra слабее ANTLR4

1. **Нет синхронизации на границах решений.** Ошибка обнаруживается только при фактическом
   терминальном mismatch; до того парсер честно тратит попытки на альтернативы, которые
   заведомо не могут сойтись (LA(1) не в их First). `_expected` накапливается только в самой
   дальней точке (`Parser.cs:794-808`) — из-за этого финальный «expecting {...}» часто грубее,
   чем мог бы быть (ANTLR4 собирает ожидаемые на границе блока/цикла).
2. **Нет дешёвого single-token deletion.** «Лишний токен» чинится только через S2 — после
   спекулятивного parse якорей. ANTLR4 закрывает этот доминирующий класс ошибок одной
   2-lookahead проверкой.
3. **Одна ошибка — и стоп.** При исчерпании лимитов `Recover` выходит с `FatalError`
   (`Parser.Recovery.cs:255-257`) и парсинг завершён. ANTLR4 после неудачного `recover()`
   **продолжает** разбор дальше: каждая ошибка — в listener, дерево — по всему вводу. Для IDE
   это принципиально: файл с 50 ошибками даёт 50 diagnostics, а не одну.
   `RecoveryDiagnostic.Kind.Unrecovered` уже существует в enum (`Recovery/RecoveryDiagnostic.cs`),
   но движком **никогда не генерируется**.
4. **Стратегия — compile-time, не runtime.** `#if RECOVERY` (`Directory.Build.props:6-12`)
   требует две сборки; ANTLR4 подменяет `_errHandler` в рантайме (`BailErrorStrategy`).
5. **Нет «тихой зоны».** Во время re-парса после принятого патча side-эффекты mismatch'ов
   (`ErrorPos`, `_expected`, snapshot) обновляются без ограничения «только дальше E»;
   преждевременный mismatch на дальней позиции (артефакт патча) может перепутать `_expected`,
   который потом уйдёт в финальное сообщение. ANTLR4 формализует это флагом
   `errorRecoveryMode`/`reportMatch`.
6. **Нет канала качества грамматики.** Счётчики есть, но автору грамматики не сообщается
   «якорь X срабатывает в N местах», «правило Y никогда не восстанавливается» и т.п.
   (у ANTLR4 — `DiagnosticErrorListener`).

### 2.4. Что CsNitra умеет, а ANTLR4 нет

- **S2 — структурная валидация resync-точки** спекулятивным parse (ANTLR4 доверяет FOLLOW —
  может «досинхронизироваться» в мусоре);
- **S4** (достроение в EOF) и **S5** (хвостовой мусор) — у ANTLR4 нет (лишние токены
  после успешного EOF просто не парсятся);
- **атомарные патчи с откатом** (`PatchLog`) — ANTLR4 мутирует состояние без права на откат;
- **декларативный recovery в грамматике**: `OftenMissed`, `RecoveryRule`, `RecoveryPrefix/Postfix`,
  `RecoveryOptions` (`TryInsert`, `Anchors`, `Terminators`, `CanStart`, `Recoverable`) —
  у ANTLR4 recovery целиком в движке, автору грамматики недоступен ни один хук;
- **memo-префикс**: повторный проход `[0..E)` — почти бесплатные memo-hits
  (`RecoveryMethods.md` §1, п. 6).

---

## 3. Что взять из ANTLR4 в `Parser.Recovery.cs`

Дополнение к `RecoveryImprovementProposal.md` (A–F). Нумерация A4-x — чтобы не конфликтовать.
Для каждого: идея (источник в ANTLR4), как маппится, где применить, эффект.

### A4-1. «Тихая зона» (аналог `errorRecoveryMode`/`reportMatch`) — приоритет: средний

**Идея ANTLR4.** Пока парсер не совпал ни с одним токеном после ошибки, side-эффекты
подавлены: ошибки не репортятся, sync не работает; режим завершается первым успешным
совпадением (`DefaultErrorStrategy.java:27, 101-133`).

**Маппинг.** Формализовать инвариант: после принятия кандидата в точке E mismatch'ы с
позицией **≤ E** не должны обновлять `ErrorPos`/`_expected` и не должны снимать snapshot —
они устарели (именно их чинит принятый патч). Новый `FailureSnapshot` — только если mismatch
строго дальше E.

**Где применить.** `CaptureSnapshot` (`Parser.Recovery.cs:486-499`) и `ReportMismatch`
(`Parser.cs:794-808`): сравнение позиции mismatch с `_recoveryPoint` (поле уже есть,
`Parser.Recovery.cs:15`) — одна проверка.

**Эффект.** Защита финального `_expected` от отравления артефактами патча; меньше
бесполезных snapshot'ов на итерациях, где ре-парс «ходит назад» по уже принятым чинкам;
повторяемый, проверяемый контракт вместо неявного поведения.

### A4-2. «Отчёт и продолжить» вместо `FatalError` при исчерпании лимитов — приоритет: высокий

**Идея ANTLR4.** Неудачный `recover()` не убивает парсинг: ошибка уходит в listener,
ресинхронизация доводит до границы, разбор продолжается до конца файла — каждая ошибка
отчитана отдельно (`DefaultErrorStrategy.java:158-181`; catch правила, `Java.stg:453-457`).

**Маппинг.** Сейчас при исчерпании лимитов (64 итерации / 3 попытки на точку) `Recover`
выходит с `FatalError` в E и парсинг завершён (`Parser.Recovery.cs:286-290, 255-257`).
Предложение: при исчерпании лимитов — **не останавливаться**, а:

1. сгенерировать `RecoveryDiagnostic(Kind.Unrecovered, E, ...)` (значение enum уже есть,
   просто не генерируется — `Recovery/RecoveryDiagnostic.cs`);
2. применить дно S5/S6 (абсорбер до known-good точки/EOF — A1 из
   `RecoveryImprovementProposal.md`);
3. продолжить итерации `Recover` дальше — следующие ошибки файла будут обработаны так же.

Итог: `ParseResult` несёт **список** diagnostics (вставлено/пропущено/не восстановлено) +
дерево по всему вводу, а не единственный `FatalError`. Для профиля `Compiler` (A3) можно
оставить старое поведение (быстрый fail) — см. A4-5.

**Эффект.** IDE получает все ошибки файла за один проход (главный выигрыш для интерактивного
использования); «не восстановилось» деградирует в «восстановлено грубо + метка Unrecovered»,
а не в «сдаюсь». Компонуется с A1/A3 из основного предложения.

### A4-3. Дешёвый кандидат single-token deletion — приоритет: средний

**Идея ANTLR4.** «Лишний токен» — доминирующий класс ошибок; чинится одной проверкой:
`LA(2) ∈ expected(E)` → съедаем `LA(1)`, отчёт «extraneous input» (`DefaultErrorStrategy.java:544-562`).

**Маппинг.** В `RecoveryEngine.Generate` — кандидат ранга **1** (между S1 и S2):
если терминал, следующий за E (после trivia), совпадает с любым из ожидаемых в E
(снимок: `Expected` кадра / `TryInsert` / `FailedTerminal`), — абсорбер ровно `[E..E+1)`
(Injection.Absorb на упавший терминал — механика уже есть в S2, `RecoveryEngine.cs:191-195`).

**Где применить.** `Recovery/RecoveryEngine.cs` — новый генератор после S1 (38-98), до
`GenerateS2` (104-298); проверка — один TryMatch следующего терминала, **без** спекулятивного
parse (в отличие от S2 с S==E+1, который требует полного proof'а якорем).

**Эффект.** Класс «опечатка-лишний-токен» (дубликат `;`, лишний оператор и т.п.) закрывается
на ранге 1 за O(1) операций, не доходя до дорогого S2; снимает нагрузку с бюджета попыток
(A2 из основного предложения).

### A4-4. First-префильтр и «ожидаемые на границе» (аналог `sync`) — приоритет: средний

**Идея ANTLR4.** Перед каждым решением — дешёвая проверка `LA(1) ∈ nextTokens(state)`;
при неудаче на границе блока/цикла ожидаемое множество фиксируется **там** (FIRST тела ∪
выход), а не в глубине первого терминального mismatch'а (`DefaultErrorStrategy.java:242-248`;
`ATN.getExpectedTokens` 166-195).

**Маппинг.** Два дёшевых улучшения:

1. **Префильтр альтернатив**: в `ParseAlternative` (`Parser.cs:490-523`) перед попыткой
   альтернативы — проверка «первый терминал/First первого элемента ∈ текущий терминал»
   (terminal cache уже кэширует `TryMatch` по `(terminal, pos)`, `Parser.cs:759-763` —
   проигрышная попытка всё равно бесплатна, но префильтр избавляет от кадров и
   snapshot-шума);
2. **Ожидаемые на границе цикла**: в начале итерации `ZeroOrMany`/`OneOrMany`/`SeparatedList`
   при `LA(1) ∉ First(тело)` — записать в `_expected` не пустоту, а `First(тело) ∪ exit-токены`
   (First-множества уже посчитаны `FollowSetCalculator.cs`, подъём по кадрам —
   `GetTerminators`).

**Эффект.** (1) меньше шума в `_expected`/snapshot'ах от заведомо проигрышных альтернатив;
(2) финальный «expecting {...}» в `FatalError` и diagnostics становится точным на границах
циклов — как раз те места, где ANTLR4 через `sync` ловит ошибки раньше всех.

### A4-5. Стратегия в рантайме вместо `#if RECOVERY` (аналог `BailErrorStrategy`) — приоритет: высокий

**Идея ANTLR4.** Поведение при ошибке — подменяемый объект, а не вариант сборки:
`DefaultErrorStrategy` vs `BailErrorStrategy` (`Parser.java:101`; `BailErrorStrategy.java:39-72`);
two-stage parsing — bail-проход как дешёвая «валидация», recovery-проход только на битом
вводе (javadoc `ParserATNSimulator.java:201-248`).

**Маппинг.** Это runtime-подтверждение A3 (`RecoveryProfile`) из основного предложения;
ANTLR4 добавляет конкретную форму:

- `RecoveryProfile.Mode.Compiler` = **bail**: без `RecoveryEngine`, без кандидатов — первый
  `FatalError` (или `Recoverable:false`) завершает парсинг сразу. Эквивалент текущего
  `Parser.NoRecovery.cs`, но **без второй сборки** и без потери depth-guard (текущий
  no-recovery режим теряет guard глубины — `Parser.NoRecovery.cs:45`, дефект из
  `RecoveryImprovementProposal.md` R3);
- two-stage для компилятора: bail-проход → при ошибке — recovery-проход того же файла.
  На корректном коде (99% сценариев) recovery-проход не запускается — латентность нулевая.

**Эффект.** Одна сборка движка покрывает IDE/компилятор/тесты; устраняет compile-time
развилку и её побочный эффект (пропавший depth-guard); делает «быстрый fail» свойством
профиля, а не бинарника.

### A4-6. Канал качества грамматики (аналог `DiagnosticErrorListener`) — приоритет: низкий

**Идея ANTLR4.** Отдельный от пользовательских ошибок поток: симулятор сообщает автору
грамматики, где предсказание неоднозначно/контекстно-зависимо
(`DiagnosticErrorListener.java:37-152`, `reportAmbiguity`/`reportContextSensitivity`,
`ParserATNSimulator.java:2135-2160`).

**Маппинг.** На базе счётчиков D2 из основного предложения — публичный
`GrammarDiagnostics` (не путать с `RecoveryDiagnostics` — те про ошибки **ввода**):

- якорь X использован N раз / никогда (проверка качества `Anchors`);
- правило Y: все recovery-точки в нём — одна и та же (вероятный дефект грамматики, а не входа);
- `Recoverable:false` регион никогда не срабатывает на корпусе (мёртвая строгость);
- S2-спекуляции всегда заканчиваются одним и тем же якорем → якорь избыточен.

**Эффект.** Авторы грамматики (C#, будущие языки) получают обратную связь по качеству
recovery-разметки, как ANTLR4-авторы получают отчёты об амбивалентности. Дёшево: всё уже
собирается в счётчиках.

### A4-7. Точный expected-набор в финальном сообщении — приоритет: низкий

**Идея ANTLR4.** Ожидаемое множество для отчёта — FIRST текущего состояния + подъём по
контексту с объединением FOLLOW, пока не исчезает EPSILON, + EOF
(`ATN.getExpectedTokens`, `ATN.java:166-195`).

**Маппинг.** `FatalError` в `FinalizeResult` (`Parser.Recovery.cs:255-257`) сейчас несёт
`_expected` (только самая дальняя терминальная точка). Улучшить: при наличии
`FailureSnapshot` в E — expected = `Expected` верхнего кадра **∪** `GetTerminators(стек)`
**∪** First-терминалы суффиксных обязательств (механика уже есть в S2/S4,
`RecoveryEngine.cs:215-239` — вынести в общую функцию).

**Эффект.** «expecting {...}» в `FatalError` и в `Unrecovered`-диагностике (A4-2) отражает
весь контекст, а не один упавший терминал; качество squiggle/сообщения IDE.

### Чего из ANTLR4 брать **не стоит**

- **Мутация состояния без отката** (весь `DefaultErrorStrategy` мутирует parser прямо):
  `PatchLog`/`RollbackPatches` CsNitra — строже (кандидат либо принят, либо как будто не было);
- **Принятие чинки локальной эвристикой** (insertion/deletion без проверки «двинулся ли парсинг»):
  валидация re-парсом (I1) — сильная сторона CsNitra, ANTLR4 здесь слабее;
- **ATN-специфика** (`sync` по типу ATN-состояния, отложенный NoViableAlt, SLL/LL-машина):
  прямого аналога в longest-match парсере нет — брать только *дух* (границы, тихая зона),
  а не реализацию;
- **`recoverLine`/`consumeErrorEvent`** — ANTLR3-артефакты, в ANTLR4 уже отсутствуют;
- **«проглотить один символ» в лексере** — в CsNitra уже покрыто R1 (мусорный терминал)
  из основного предложения, не дублировать.

---

## 4. Итог и приоритеты

| # | Идея | Источник (ANTLR4) | Эффект | Оценка |
|---|---|---|---|---|
| A4-2 | «отчёт и продолжить»: список ошибок, `Unrecovered` вместо `FatalError`-стопа | catch правила + `recover()`, continuation | все ошибки файла за один проход (IDE) | средняя (компонуется с A1) |
| A4-5 | runtime-стратегия (bail) вместо `#if RECOVERY` | `BailErrorStrategy`, two-stage | одна сборка, быстрый fail как профиль, возврат depth-guard | средняя |
| A4-1 | тихая зона: side-эффекты только дальше принятой точки E | `errorRecoveryMode`/`reportMatch` | защита `_expected`, меньше snapshot'ов | маленькая |
| A4-3 | single-token deletion как кандидат ранга 1 | `singleTokenDeletion` | доминирующий класс ошибок за O(1) | маленькая |
| A4-4 | First-префильтр + ожидаемые на границе циклов | `sync()` | точнее «expecting {...}», меньше шума | маленькая |
| A4-7 | точный expected в финальном сообщении | `ATN.getExpectedTokens` | качество диагностики | маленькая |
| A4-6 | канал качества грамматики | `DiagnosticErrorListener` | обратная связь авторам | маленькая |

Рекомендуемое встраивание в план из `RecoveryImprovementProposal.md`:

- A4-2 и A4-5 — в волну **1–4** (вместе с A1/S6 и A3/профилями — это одна тема «контракты
  результата по профилям»);
- A4-1, A4-3, A4-4 — в волну **5** (дешёвые, независимые, измеряются по D1-корпусу);
- A4-6, A4-7 — в волну **7** (полировка).

Главный урок ANTLR4 для CsNitra в одном предложении: **ошибка — событие, а не приговор**;
парсер обязан довести разбор до конца файла и отчитаться о каждой ошибке отдельно,
а «сдался» — только осознанная настройка профиля, не результат исчерпания лимитов.

---

## 5. Сравнительный анализ: ANTLR4 vs Roslyn

Примечание о путях: строки Roslyn ниже — по актуальному чекауту `C:\RSDN\roslyn`
(dotnet/roslyn master, 2025). В старом `Roslyn.md` и в R1–R6 `RecoveryImprovementProposal.md`
приведены строки старой версии: например, hotspot'ы `StackGuard.EnsureSufficientExecutionStack`
были 2593/3237/8352/11475, в текущем коде — 2577/3226/8293/11436 (первый — 241 — не изменился).
`LanguageParser.Recover`/`RecoverAndRestart` в новом Roslyn уже удалены: recovery — это
семейство `SkipBad*` + `EatToken*` + `ConsumeUnexpectedTokens` + stack-guard.

### 5.1. Механизмы по одному (из §1)

| Механизм | ANTLR4 | Roslyn (file:line) | Совпадение / расхождение |
|---|---|---|---|
| errorRecoveryMode / reportMatch | флаг strategy: отчёты подавлены до первого успешного match (DefaultErrorStrategy.java:27, 101-133) | флага нет; аналог — «одна диагностика на skip-цикл»: ошибка только на **первом** плохом токене, остальные съедаются молча (LanguageParser.cs:4597, 4623) | совпадает по цели (подавление шума); у Roslyn — структурно в цикле, у ANTLR — режимным флагом |
| sync на границах | sync() перед каждым decision: дешёвый LA(1)∈nextTokens, delete/эскалация (DefaultErrorStrategy.java:230-286) | sync до решения нет; аналог — skip-цикл со стопом по контексту **после** mismatch (SkipBadTokensWithExpectedKind, LanguageParser.cs:4579-4604) + IsPossible* (4434-4476) | расхождение: ANTLR не пускает в заведомо проигрышную ветку, Roslyn реагирует внутри цикла списка |
| single-token insertion | singleTokenInsertion: LL2-проверка, нулевой `<missing>`-токен (508-523) | EatToken → CreateMissingToken: нулевой missing-токен, без lookahead (SyntaxParser.cs:521-534, 552-556) | совпадает: нулевая вставка; ANTLR проверяет LA(1)∈nextTokens(nextState), Roslyn доверяет коду правила |
| single-token deletion | singleTokenDeletion: LA(2)∈expected (544-562) | отдельной операции нет: «лишний» токен съедается skip-циклом и прикрепляется как SkippedTokensTrivia (SyntaxParser.cs:1018-1102) | расхождение: ANTLR — дешёвая O(1) чинка, Roslyn — skip-цикл (для CsNitra — идея A4-3) |
| runtime context-sensitive FOLLOW | getErrorRecoverySet: объединение FOLLOW по фактическому стеку вызовов (741-756) | TerminatorState — 29 флагов (57-90) + IsTerminator (94-137) + CanStart*/IsPossible* (2116, 9181-9185) | совпадает: контекстный стоп-набор; ANTLR считает из ATN, Roslyn — hand-written на правило |
| ErrorNode-пометка | ErrorNodeImpl: проглоченный токен — error-узел (Parser.java:568-593) | пропущенные токены — SkippedTokensTrivia на ближайшем реальном токене (AddSkippedSyntax, SyntaxParser.cs:1018-1102); missing — IsMissing | совпадает: дерево полное по вводу; расхождение: узел vs trivia (см. F, «чего не брать») |
| отложенный точный отчёт | nextTokensContext на границе, expected считается ATN.getExpectedTokens (250-258; ATN.java:166-195) | отложенного нет: диагностика рождается в момент mismatch с **фактическим** видом («expected X, found Y», SyntaxParser.cs:552-556, 674-689) | расхождение: «expecting» у ANTLR точнее, у Roslyn — немедленный |
| отложенный NoViableAlt | getSynValidOrSemInvalidAltThatFinishedDecisionEntryRule (ParserATNSimulator.java:1283-1302) | аналога нет (нет ATN); ближайший — IsPossible* через reset point (SyntaxParser.cs:158-217) | расхождение: специфика ANTLR; у Roslyn — спекулятивное lookahead |
| failsafe прогресса | lastErrorIndex/lastErrorStates: между ошибками ≥1 токен/состояние (164-175) | IsMakingProgress (SyntaxParser.cs:1178-1189) + «всегда ≥1 токен» (SkipBadMemberListTokens, LanguageParser.cs:2106-2109) + полный поток токенов | совпадает: вхолостую не крутится; у Roslyn гарантия **структурная** — лексер не выдаёт «пустого» токена |
| lexer не умирает | LexerNoViableAlt → отчёт + пропуск 1 символа + SKIP (Lexer.java:112-165, 352-357) | BadToken (Lexer.cs:406-408) + лимит 200, дальше весь остаток файла одним токеном (710-732); runaway (Lexer_StringLiteral.cs:1143-1148) | совпадает: полный поток; дно у Roslyn сильнее (один BadToken на остаток файла) |
| strategy / bail | BailErrorStrategy — подменяема в рантайме (BailErrorStrategy.java:39-72) | аналога нет: один режим «всегда recovery» (IDE и компилятор парсят одинаково) | расхождение: у Roslyn нет fail-fast парсинга (A4-5 не заимствована из Roslyn) |
| two-stage | SLL→LL failover + bail-проход (PredictionMode.java:24-83; ParserATNSimulator.java:201-248, 313-381) | отсутствует: один проход до конца; «быстрое» = fast-path лексера (QuickScanner.cs) + инкрементальный blending (Blender.cs) | расхождение: «всегда до конца» у Roslyn — структурное свойство, а не двухфазность |
| diagnostic-канал для автора грамматики | DiagnosticErrorListener: неоднозначность/контекстность (DiagnosticErrorListener.java:37-152) | нет: грамматика hand-written, «автора грамматики» не существует | расхождение: у Roslyn такого канала нет |

### 5.2. Где Roslyn сильнее ANTLR4 (для цели «до EOF + IDE-скорость»)

1. **Структурное дно до EOF.** Три независимых дна: skip-цикл всегда потребляет ≥1 токена
   (LanguageParser.cs:2106-2109); `ConsumeUnexpectedTokens` — «съесть до EOF» (14691-14706);
   проверка стека → `CreateForGlobalFailure` (223-234): **предварительная** проверка
   `StackGuard.EnsureSufficientExecutionStack` (Core\Portable\InternalUtilities\StackGuard.cs:25-31,
   `RuntimeHelpers.EnsureSufficientExecutionStack`, вызывается из `ParseWithStackGuard`, 207-221)
   бросает **перехватываемое** `InsufficientExecutionStackException` **до** реального переполнения,
   catch (217-221) возвращает весь файл одним BadToken'ом + пустой узел ожидаемого типа.
   Реальный `StackOverflowException` **не перехватывается** (в .NET Framework перехват не поддерживается;
   даже где перехватывается — ненадёжно): это баг, который надо предотвратить (в CsNitra — depth-guard,
   `Parser.Recovery.cs:152-156`, калибровка — R3).
   У ANTLR4 failsafe в `recover()` (164-175) защищает только recovery-цикл.
2. **Диагностика в дереве.** Диагностика прикрепляется к green-узлу (`WithDiagnosticsGreen`,
   Core\Portable\GreenNodeExtensions.cs:110), `GetDiagnostics()` — ленивая переборка дерева
   (CSharpSyntaxTree.cs:785-798, 856-859). IDE может запросить диагностику поддерева;
   у ANTLR4 — только поток listener'а.
3. **O(1)-спекулятивный lookahead.** Reset point (SyntaxParser.cs:158-217) — сохранить
   позицию токена, распарсить вперёд, откатить; 100+ мест в IsPossible*/IsDefinite*
   (4434-4476, 9141-9179). У ANTLR4 — прогон ATN-симулятора.
4. **Локальный mid-parse stack-recovery.** Точки входа `ParseWithStackGuard` (207-221):
   `ParseMemberDeclaration` → `IncompleteMember` (2554-2571), `ParseStatement` →
   `EmptyStatement` (8184-8189), `ParseExpression` → missing identifier (11059-11064) —
   overflow даёт локальный плохой узел, а не «весь файл» (взято R3).
5. **Парсер никогда не выводит токены заново** — поток полный от лексера; стоимость
   recovery = O(пропущенных токенов), никогда O(разбора).

### 5.3. Где ANTLR4 сильнее Roslyn

1. Дешёвая локальная чинка без re-парса (2-lookahead insertion/deletion,
   DefaultErrorStrategy.java:463-562); у Roslyn — только injection missing-токена и skip-циклы.
2. Считаемый context-sensitive FOLLOW (getErrorRecoverySet, 741-756); у Roslyn — 29 флагов
   и десятки лямбд hand-written на язык.
3. Runtime-стратегии (BailErrorStrategy) и two-stage; у Roslyn — нет fail-fast.
4. Канал качества для автора грамматики (DiagnosticErrorListener); у Roslyn — отсутствует.

---

## 6. Чего из Roslyn нет в CsNitra

За пределами R1–R6, A4-1..A4-7 и текущей реализации (`RecoveryMethods.md`).

### 6.1. Reset point — спекулятивное «может ли начаться тут» с O(1)-откатом — переносится частично

**В Roslyn.** `GetResetPoint`/`Reset`/`Release` (SyntaxParser.cs:158-217) +
`DisposableResetPoint` с сохранением контекстных флагов (LanguageParser.cs:14619-14666,
в `Reset` сохраняется и `_termState` — 14631). Используется 100+ раз в IsPossible*/IsDefinite*
(4434-4476, 9141-9179): «распарсить вперёд реальным парсером, откатиться, принять решение».

**В CsNitra.** Аналог есть только внутри recovery: `Speculative` с подавлением side-эффектов
(`Parser.Recovery.cs:360-377`) + scratch-парсер для якорей (`RecoveryEngine.cs:352, 104-298`)
и per-call specCache (B2). В проходе — только статические First-проверки (A4-4) и авторские
`CanStart` (S2 T2, `RecoveryMethods.md` §4 — «только авторские, не выводятся»).

**Значение.** Ограниченный спекулятивный зонд (по глубине кадров, а не по стоимости) на
границах циклов и в S2 (а) выводит CanStart для любого Ref из кадров снимка — T2 перестаёт
быть только авторским, (б) двигает E раньше (цикл останавливается на первом подозрительном
терминале, а не после всех альтернатив) — snapshot точнее. → A5-1.

### 6.2. «Аборт во внешний цикл» (PostSkipAction.Abort) — прямого аналога нет

**В Roslyn.** Skip-цикл останавливается на контекстном терминаторе **до** его потребления
(4591-4594), внутреннее правило возвращает частичный результат, внешний цикл (например
`ParseStatements`, 9141-9179) принимает решение — терминатор принадлежит внешнему контексту.

**В CsNitra.** В проходе аналога нет: циклы (ZeroOrMany/ParseSeparatedList, `Parser.cs:627-700`)
имеют только zero-progress guard (603, 652, 700), «skip + стоп» происходит на re-парсе (S2/S3).
Re-парс сам по себе и есть «внешний цикл» — механизм не отсутствует, а **поглощён архитектурой**;
ценность идеи забирают R2 (стоп-предикат) и A4-4 (границы).

### 6.3. Диагностика как свойство дерева — в CsNitra ручной список

**В Roslyn.** Диагностика на узле (GreenNodeExtensions.cs:110), `GetDiagnostics` — переборка
дерева (CSharpSyntaxTree.cs:856-859); дерево — единственный источник правды.

**В CsNitra.** `RecoveryDiagnostics` — плоский список, накапливаемый во время recovery
(`Parser.Recovery.cs:21-22`, пополнение 323/330), при этом дерево уже помечает recovery-узлы
(`TerminalNode` IsRecovery/IsAbsorber, `SyntaxTree.cs:149-170`). Данные дублируются: список и
дерево могут расходиться (откат, итерации). → A5-2.

### 6.4. Полная форма размещения диагностики missing-узла — расширение R5

**В Roslyn.** `GetDiagnosticSpanForMissingNodeOrToken` (SyntaxParser.cs:783-880): missing-узел,
**содержащий пропущенный текст**, получает диагностику на **первом пропущенном токене**
(852-880), а не на всём спане; случай «конец предыдущей строки» — 811-833. R5 забирает только
второй случай (Span в `EatTokenEvenWithIncorrectKind.getDiagnosticSpan`, 622-635).

**Значение.** Короткий squiggle на первом «виновном» слове грязного региона, а не подчёркивание
всего региона. → A5-3.

### 6.5. Типостабильное дно результата — Roslyn всегда возвращает узел ожидаемой формы

**В Roslyn.** `CreateForGlobalFailure` (LanguageParser.cs:223-234) — результат всегда валидный
узел ожидаемого типа (CompilationUnit с EOF, IncompleteMember, EmptyStatement, missing
identifier) + диагностика на узле + `ForceEndOfFile` (SyntaxParser.cs:514-517). Триггер — catch
`InsufficientExecutionStackException` в `ParseWithStackGuard` (LanguageParser.cs:217-221); реальный
stack overflow не перехватывается (см. §5.2). У вызывающего кода (IDE) нет особой ветки «парсер умер».

**В CsNitra.** R3/A1 дают дно *дерева* (корневой абсорбер), но *результат* (Success/Failed,
`ParseResult.cs:3-16`) и форма корневого узла не зафиксированы: `Failed(FatalError)` — другая
ветка API. → A5-4.

### 6.6. «Мягкий сепаратор» в separated list — чужой сепаратор с диагностикой

**В Roslyn.** `allowSemicolonAsSeparator`: `;` на месте `,` потребляется с диагностикой и
трактуется как запятая (LanguageParser.cs:14551-14559) — декларативный опция парсера списков,
не hand-written лямбда.

**В CsNitra.** Автор может добавить альтернативный сепаратор в грамматике (терминал `Sep`);
встроенного «чужой сепаратор → диагностика + продолжить» для `SeparatedList` нет. → A5-5.

### 6.7. PreLex — аналога нет, ценность ~0

Прелексирование первых min(4096, len/2) токенов до старта парсинга (SyntaxParser.cs:138-156,
`CachedTokenArraySize` — 43). В CsNitra нет потока токенов (longest-match по символам), а префикс
после первого прохода всё равно — memo-hit'ы.

### 6.8. Blender (инкрементальный) — настоящий механизм IDE-скорости, не recovery

Скорость live-редактирования в Roslyn даётся в первую очередь не recovery, а инкрементальным
blending: неповреждённые поддеревья старого дерева переиспользуются (Blender.cs;
SyntaxParser.cs:27-49, 130-137, EatNode). Roslyn.md §4.5 уже фиксирует, что Blender вынесен
отдельно от recovery. Для CsNitra это видимый разрыв для «живого редактирования»: на каждой
итерации `Recover` суффикс от E перепарсивается заново (префикс — memo-hit), структурного
переиспользования нет (нет identity дерева для IDE-функций). Перенос = отдельный большой
проект, не часть recovery (см. 6.9).

### 6.9. Что переносится плохо (честно, без героизации)

1. **Hand-written стоп-предикаты** (TerminatorState — 29 флагов + лямбды isNotExpected/abort) —
   антипод grammar-driven; F уже фиксирует «не брать». Ответ CsNitra — R2 (предикат из
   вычисляемых First/Follow-множеств).
2. **Один проход без re-парса.** Работает, потому что поток токенов — чистая функция позиции
   (лексер детерминирован и контекстно-независим). При longest-match набор терминалов,
   испытываемых в p, зависит от правила → «вывести токены заново» = re-парс. Re-парс + memo —
   не дефект, а единственно корректная модель для CsNitra (Roslyn.md §4.2).
3. **O(1)-откат потока** (reset point) — по той же причине: «поток терминалов» в CsNitra
   откатывать нельзя. Scratch-парс + `Speculative` — аналог, но дороже (O(разбора) без
   memo-hit). Разрыв — в стоимости, а не в возможности.
4. **Trivia для пропущенного** — абсорбер отдельным узлом обязателен по тайлингу I4; в модели
   дерева CsNitra «trivia» не отдельная структура (F, «чего не брать»).
5. **PreLex лексера** — нет потока, аналога нет.

---

## 7. Дополнительные предложения по улучшению CsNitra (A5-x)

Дополнение к A4-x и R1–R6. Для каждого: идея (источник в Roslyn с file:line), маппинг на
CsNitra, эффект, приоритет, оценка.

### A5-1. Ограниченный спекулятивный зонд как стоп-предикат в проходе (аналог IsPossible*) — приоритет: средний

**Источник.** Reset point + IsPossible* (SyntaxParser.cs:158-217; LanguageParser.cs:4434-4476,
9141-9179).

**Маппинг.** (a) `RecoveryEngine.cs` GenerateS2 (104-298): T2 CanStart сейчас только авторский —
выводить автоматически для любого Ref из кадров снимка: ограниченный спекулятивный parse правила
(лимит глубины кадров, напр. ≤3; механика — существующий `Speculative`, Parser.Recovery.cs:360-377,
+ terminal cache; результат (Ok, EndPos) — в specCache из B2). (b) Циклы `Parser.cs` (627-700):
на recovery-позиции — First-фильтр (A4-4) + зонд, если позволяет бюджет.

**Эффект.** T2 перестаёт быть только авторским → больше resync-точек; E раньше (цикл
останавливается на первом подозрительном терминале) → точный `FailureSnapshot`, меньше
бесполезных попыток на «грязном» регионе. Для IDE — быстрее recovery на файлах с многими
ошибками подряд (компонуется с B5).

**Оценка.** Средняя (зонд + интеграция в S2/циклы + параметр бюджета).

### A5-2. Диагностика, выводимая из дерева (единый источник правды) — приоритет: средний

**Источник.** Диагностика на узле (Core\Portable\GreenNodeExtensions.cs:110;
CSharpSyntaxTree.cs:785-798, 856-859).

**Маппинг.** `Parser.Recovery.cs:21-22`: `_recoveryDiagnostics` оставить как кэш, добавить
`DeriveDiagnostics(root)` — обход IsRecovery-узлов (вставка нулевой ширины → `Inserted`,
абсорбер → `Skipped` с текстом узла) + `Unrecovered` из результата (A4-2). Публичное
`RecoveryDiagnostics` возвращает производное (или проверенный кэш). Дерево (`TerminalNode`
IsRecovery/IsAbsorber, `SyntaxTree.cs:149-170`) — единственный источник правды.

**Эффект.** Защита от рассинхрона список/дерево (откаты, итерации); запрос диагностики
*поддерева* (IDE: что изменилось в регионе); список становится чистой функцией дерева —
проще тестировать (D2-корпус).

**Оценка.** Маленькая.

### A5-3. Расширение R5: диагностика на первом слове грязного региона — приоритет: низкий

**Источник.** `GetDiagnosticSpanForMissingNodeOrToken` (SyntaxParser.cs:783-880): узел с
пропущенным текстом → диагностика на первом пропущенном токене (852-880).

**Маппинг.** Дополнение R5 (`RecoveryImprovementProposal.md` §F): span `RecoveryDiagnostic`
для абсорбера `[E..S)` — не весь спан, а первое небелое «слово» внутри `[E..S)` (превью уже
есть — `RecoveryEngine.cs:712`); для вставок с областью (S2) — одна из двух диагностик
(Inserted/Skipped) на первом токене.

**Эффект.** Качество squiggle в IDE: короткая метка на первом «виновном» слове, а не
подчёркивание всего пропущенного региона.

**Оценка.** Маленькая.

### A5-4. Дно-контракт результата: всегда полное дерево ожидаемой формы — приоритет: высокий

**Источник.** `CreateForGlobalFailure` (LanguageParser.cs:223-234) + `ForceEndOfFile`
(SyntaxParser.cs:514-517) + дно-узлы ожидаемого типа (2554-2571, 8184-8189, 11059-11064).

**Маппинг.** Дополнение к A1 (S6) и A4-2 («отчёт и продолжить»): после исчерпания recovery
(и при срабатывании depth-guard — R3: *предварительное* `InsufficientExecutionStackException`;
реальный stack overflow неперехватываем и является багом грамматики/recovery, устраняемым
откалиброванным guard'ом) в IDE-профиле итог = `Success<T>` с деревом, покрывающим весь
ввод (абсорберы по I4), а **не** `Failed(FatalError)` (`ParseResult.cs:14-16`); `Failed`
остаётся только в Compiler-профиле (A3/A4-5). Диагностика `Unrecovered`/`InsufficientStack`
лежит на дереве (A5-2).

**Эффект.** «100% до EOF» на уровне API: у IDE-кода нет ветки Failed «парсер сдался»;
форма дерева стабильна (корневой узел start-правила всегда есть) — упрощается обход
(комплиты, навигация, diff'ы регионов).

**Оценка.** Маленькая (семантика `FinalizeResult`, Parser.Recovery.cs:238-258, + контракт-тесты, C1).

### A5-5. Опция SoftSeparator для SeparatedList — приоритет: низкий

**Источник.** `allowSemicolonAsSeparator` (LanguageParser.cs:14551-14559) — чужой сепаратор
потребляется с диагностикой и трактуется как нужный.

**Маппинг.** `Rules.cs` / `Parser.cs` (ParseSeparatedList): опция `SoftSeparator(t)` на правиле
списка — терминал `t`, потребляемый на позиции сепаратора с диагностикой `Skipped`
(«ожидалось {sep}, получено {t}») и трактуемый как сепаратор. Декларативно, а не hand-written
лямбдой — grammar-driven аналог опции Roslyn.

**Эффект.** Класс ошибок «не тот сепаратор» (`;`/`.`/`:` в списках) закрывается инлайн
(S0 на re-парсе) одной диагностикой, без skip'а S2/S3.

**Оценка.** Маленькая.

### Приоритеты A5-x и встраивание в план

| # | Идея | Источник (Roslyn) | Эффект | Приоритет | Оценка |
|---|---|---|---|---|---|
| A5-4 | дно-контракт: всегда полное дерево ожидаемой формы (Success в IDE-профиле) | CreateForGlobalFailure (LanguageParser.cs:223-234), ForceEndOfFile (SyntaxParser.cs:514-517) | «100% до EOF» на уровне API | высокий | маленькая |
| A5-1 | ограниченный спекулятивный зонд: выводимый CanStart + стоп-предикат в проходе | reset point (SyntaxParser.cs:158-217), IsPossible* (LanguageParser.cs:4434-4476) | больше resync-точек, E раньше | средний | средняя |
| A5-2 | диагностика, выводимая из дерева | node-attached diagnostics (GreenNodeExtensions.cs:110; CSharpSyntaxTree.cs:856-859) | единый источник правды, запрос по поддереву | средний | маленькая |
| A5-3 | диагностика на первом слове грязного региона | GetDiagnosticSpanForMissingNodeOrToken (SyntaxParser.cs:783-880) | качество squiggle | низкий | маленькая |
| A5-5 | SoftSeparator для SeparatedList | allowSemicolonAsSeparator (LanguageParser.cs:14551-14559) | класс «не тот сепаратор» инлайн | низкий | маленькая |

Встраивание в волны `RecoveryImprovementProposal.md`:

- A5-4 — волны **1–2**, вместе с A1 (S6) и A4-2: одна тема «контракт результата по профилям»;
- A5-1 — волна **5**, вместе с A4-4 (границы, дешёвые проверки) и B2 (specCache — хранилище зонда);
- A5-2 — волна **6**, вместе с D2 (счётчики) — выводимость проверяется по метрикам;
- A5-3, A5-5 — волна **7** (полировка).

Главный урок Roslyn сверх R1–R6 в одном предложении: **recovery — не отдельная «фаза», а
свойство каждого цикла и каждого узла** — цикл всегда потребляет, диагностика живёт на узле,
результат всегда полное дерево ожидаемой формы; а IDE-скорость даёт не recovery, а то, что
неизменённый префикс/поддерево не перепарсивается вовсе.

---

## 8. Якоря и циклы: ANTLR4 vs CsNitra

### 8.1. Как находятся точки ресинхронизации (якоря)

**ANTLR4.** Понятия «якоря» нет. Точки синхронизации — *структурные и универсальные*:
сгенерированный код вызывает `sync()` перед каждым subrule/циклом и на каждой итерации
(javadoc `DefaultErrorStrategy.java:186-187`). Множество целей ресинхронизации считается в
рантайме: `getErrorRecoverySet()` = объединение `nextTokens(followState)` по **фактическому стеку
вызовов** (`DefaultErrorStrategy.java:741-756`). Итог: **каждое место вызова правила** — точка
ресинхронизации по умолчанию; ни авторских аннотаций, ни предвычисления, ни расходов в
нормальном режиме (кэш `nextTokens`, `ATN.java:95-100`).

**CsNitra.** Якоря *явные и разреженные* (`RecoveryEngine.cs`):
- T1: авторские `RecoveryOptions.Anchors` ближайшего кадра (113-117) + **выводимые из циклов** —
  `DeriveLoopAnchors` (301-318): только ZeroOrMany/OneOrMany/SeparatedList **с телом-Ref'ом**
  (проход по кадрам снимка, 119-127);
- T2 `CanStart` — только авторские (129-135);
- якорей нет вовсе → S2 не генерирует ничего (137-138).

Расхождение подходов: ANTLR4 *доверяет* FOLLOW-объединению (может «досинхронизироваться» в мусор);
CsNitra *доказывает* каждую точку полным спекулятивным парсом (First-pre-filter 153-159 +
scratch-парсер 143-151, 277-278) — в мусор не попадём никогда, но множество точек по умолчанию
разреженнее.

**Разрыв и его закрытие.** В цепочке нециклических правил (списки членов без явных `Anchors`)
resync-точки CsNitra — только терминаторы S3, и panic-скан может проглотить больше, чем
FOLLOW-объединение ANTLR4. Закрытие уже в плане: **A5-1** — выводить CanStart для любого Ref
из кадров снимка. Уточнение к A5-1: выводить не только T2-CanStart, но и **T1-якоря** для любого
Ref-кадра (а не только для циклов) — это и есть «каждая граница правила — точка resync» как
**дефолт**, а не опция автора; стоимость ограничивается бюджетом (specCache из B2, тиры из A2).

### 8.2. Что происходит в циклах

**ANTLR4** (`sync`, `DefaultErrorStrategy.java:230-286`):
- **голова цикла** (STAR_LOOP_ENTRY/BLOCK_START, 261-270): LA(1) ∉ nextTokens → сначала
  **single-token deletion** (лишний токен у головы цикла съедается инлайн, 266), иначе
  `InputMismatchException` → catch → `recover()` = consume до `getErrorRecoverySet` — то есть
  **выход из цикла** до FOLLOW цепочки вызовов;
- **loop-back** (STAR_LOOP_BACK/PLUS_LOOP_BACK, 272-280): `reportUnwantedToken` +
  `consumeUntil(expecting ∪ getErrorRecoverySet)` — «we opt to stay in the loop as long as possible»
  (javadoc 206-208): съедаем только до начала следующей итерации или до выхода из цикла.
  Историческая мотивация (212-222): лишний токен между членами
  `classDef : 'class' ID '{' member* '}'` не должен заставлять проглатывать до следующего класса,
  а только до следующего члена.

**CsNitra**:
- проход: циклы имеют только zero-progress guard (`Parser.cs:603, 652, 700`); инлайн-обработки
  лишнего токена у головы цикла нет;
- recovery: у S2 (ранг 2) — выводимые якоря циклов; падение в **начале итерации**
  (`topIdx == 0`), где правило совпадает с якорем → абсорбер **на уровне цикла** (memo-патч
  правила цикла в e, `RecoveryEngine.cs:177-183`) — внешний цикл пропускает `[e..S)` как одну
  итерацию и возобновляется с чистого терминала. Это и есть семантика «stay in the loop»
  ANTLR4 — но с *доказательством* точки (спекулятивный парс), а не доверием;
- S3 (ранг 3, panic-скан до терминаторов со счётчиком скобок 400-416) может выйти из цикла,
  но выбирается, только когда S2 ничего не нашёл (порядок рангов);
- суффиксные обязательства в точке resync (215-239) — у ANTLR4 аналога нет.

**Вывод по циклам: CsNitra не хуже.** «Остаться в цикле, пока возможно» реализовано
(3.0c loop-level абсорбер + якоря циклов + ранг 2 < 3). Для паритета с ANTLR4 в проходе не
хватает двух дешёвых вещей: (a) First-проверки на старте итерации с фиксацией ожидаемых на
границе (A4-4), (b) лишнего токена у головы цикла как кандидата ранга 1 (A4-3). Обе уже в плане.

### 8.3. Итог: чтобы быть «не хуже ANTLR4 и Roslyn»

| Идея (источник) | Статус в CsNitra |
|---|---|
| Каждая граница правила — точка resync (ANTLR4, `getErrorRecoverySet` 741-756) | не дефолт → **A5-1** (расширить: T1-якоря для любого Ref-кадра) |
| Дешёвая проверка + deletion у головы цикла (ANTLR4, `sync` 261-270) | нет в проходе → **A4-4** + **A4-3** |
| «Остаться в цикле, пока возможно» (ANTLR4, 272-280) | **уже есть** (3.0c, `RecoveryEngine.cs:177-183`) |
| Context-sensitive FOLLOW с фактического стека (ANTLR4, 741-756) | **уже есть** (`GetTerminators`, `FollowSetCalculator.cs:230-247`) |
| Доказательство resync-точки (Roslyn reset point; своя механика CsNitra) | **уже есть** (S2 спекулятивный парс) — CsNitra сильнее ANTLR4 |
| Полный поток токенов, «токены не выводить заново» (Roslyn) | не переносится (longest-match) — re-парс + memo — корректная модель (§6.9) |

Структурно новое не требуется: идеи ANTLR4 покрыты A4-3, A4-4, A5-1; единственное
дополнение к плану — **расширить A5-1** (T1-якоря для любого Ref-кадра как дефолт, с бюджетом).

---

## 9. Решения: per-call-site FOLLOW и паник-дно (расширяет заключение §8.3)

Итоги обсуждения пер-кол-сайт FOLLOW, panic-фолбэка и TDOPP. Дополнение к §7 (A5-x)
и к A1 `RecoveryImprovementProposal.md`.

### A5-6. Per-call-site FOLLOW для recovery — решено, приоритет средний, волна 5 (вместе с A4-4/A5-1)

**Решение.** В recovery-потребителях заменить rule-level `follow(правило кадра)` на
per-call-site значение, вычисляемое **на лету по стеку снимка** (аналог
`getErrorRecoverySet`, `DefaultErrorStrategy.java:741-756`):

```
follow_site(кадр i) = First(хвост Seq родителя после элемента-вызова)
                      ∪ (хвост nullable ? follow_site(кадр i-1) : ∅)
база: внешний кадр → {EOF}
```

- **Кто потребляет:** `GetTerminators` (стоп-набор S3, `FollowSetCalculator.cs:230-247`) и
  `Follow(top)` (источник вставок S1). Глобальный fixed-point (`ComputeFollowSets`,
  249-305) не меняется — он нужен First/Follow-префильтрам.
- **Данные уже в снимке:** место вызова кадра i = (RuleName, ElementIndex) кадра i-1
  (какой элемент Seq — вызов); для циклового кадра — `whatFollows` именно этого вхождения
  цикла (per-site версия `ProcessLoopNode`, `FollowSetCalculator.cs:352-407`).
- **Стоимость:** O(глубины стека) на recovery-событие — ничто.

**Граница применимости (важно).**
- Seq/Loop-кадры — per-call-site (частый случай: statement-циклы, списки членов, блоки).
- **TDOPP-кадры** (`PostfixFrameLocation`, `Parser.cs:441`) — формула **неприменима**:
  (a) родительский кадр указывает на синтетический `Seq` из `RuleWithPrecedence`
  (`Rules.cs:416, 437-444`), которого нет в `Rules[rule]` — адрес не резолвится;
  (b) множество продолжения операнда зависит от runtime `minPrecedence`
  (условие применимости, `Parser.cs:360`) — статического «хвоста» у while-цикла постфиксов нет;
  (c) кадр не кодирует альтернативу оператора. → **rule-level фолбэк = текущее поведение,
  осознанно**: нужное там — внешний follow, который rule-level и даёт; циклолокальное
  восстановление TDOPP покрывается `RecoveryPrefix`/`RecoveryPostfix` + сбросом точки
  recovery (`Parser.cs:397-402`), а не S3-терминаторами.
- **ContextScope** (контекстно-зависимые терминалы) — аналогично rule-level: статические
  множества там и так приближение, per-site не улучшает.
- Не удалось восстановить место вызова → rule-level `follow(правило)` — безопасный фолбэк,
  текущее поведение сохраняется.

**Эффекты.**
1. Одиночный кандидат S3 (`RecoveryEngine.cs:444-445`) становится **звучным**: любая точка
   остановки — истинная sync-точка (токен, после которого разбор законно продолжается);
   ложные остановки от супerset'а исчезают.
2. A5-8 (K-кандидаты) понижается до опциональной защиты.
3. Бонус: более узкий `Follow(top)` напрямую бьёт по A2 — S1 генерирует до
   `|FollowSet|+2` кандидатов и выедает бюджет; точный набор меньше и качественнее.
4. Не заменяет A5-7 (паник-дно) — задача ортогональна (нет snapshot / длинный регион).

**Оценка.** Маленькая-средняя: 100–150 строк в `FollowSetCalculator` + интеграция в
`GetTerminators` + регресс-тест на примере `List := Ref(X) ',' Ref(X)` / `Block := Ref(X) '}'`
(ложная остановка на `'}'` в контексте List).

### A5-7. Уточнение A1: S6 = паник-дно (semantics ANTLR4 `recover`) — волна 1–2 (вместе с A1/A4-2)

**Решение.** С6 (гарантированное дно) получает семантику panic mode:
- стоп-набор = терминаторы (per-call-site FOLLOW по A5-6, с rule-level фолбэком)
  ∪ anchor-First (текущий дешёвый First-скан A1);
- **без требования snapshot** (синтетический кадр start-правила: терминаторы = follow(start));
- **без MaxSkip** (или предел из профиля, A3) — `consumeUntil` у ANTLR4 безграничен;
- строгие регионы (`Recoverable: false`) остаются исключением — дно не «спасает» через них (C1).

**Почему.** S3 уже является panic mode (context-sensitive FOLLOW-объединение,
`GetTerminators`; скан со счётчиком вложенности, `RecoveryEngine.cs:364-511`), но ограничен:
snapshot==null (хвостовой мусор без mismatch на границе) → S3 не генерируется; регион
> `MaxSkip` (1000) → ничего не найдёт. Дно закрывает именно эти случаи: «always-to-EOF»
ANL4-стиля (ошибка — событие, а не приговор).

### A5-8. K-кандидатов в S3 — опциональная защита, низкий приоритет (волна 7 или не делать)

До A5-6 была бы главной чинкой «ложной первой остановки» (S3 генерирует ровно первый
стоп-пункт; ложный = единственный кандидат отвергнут = смерть итерации). После A5-6
стоп-набор точен → один кандидат звучен; K ближайших остановок (не `break` на первой)
остаётся дешёвой защитой в глубину.

### Влияние на план

| # | Что | Волна | Компонуется с |
|---|---|---|---|
| A5-6 | per-call-site FOLLOW для recovery (с границей TDOPP/ContextScope) | 5 | A4-4, A5-1 |
| A5-7 | S6 = паник-дно (уточнение A1) | 1–2 | A1, A4-2, A3 (профили) |
| A5-8 | K-кандидатов в S3 | 7 / опционально | A5-6 |

A5-4 (дно-контракт результата) не меняется: A5-7 даёт дну *деревьев* panic-семантику,
A5-4 — контракт *результата* (`Success<T>` в IDE-профиле).
