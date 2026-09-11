# Система восстановления после ошибок — План v2

**Статус:** заменяет `RecoverySystemPlan.md` (v1). Чек-лист (`RecoverySystemChecklist.md`) переписать по Фазам v2.

Код, на который ссылаются `file:line` ниже: `ExtensibleParser/Parser.cs`, `ExtensibleParser/Rules.cs`, `ExtensibleParser/Result.cs`, `ExtensibleParser/SyntaxTree.cs`, `ExtensibleParser/FollowSetCalculator.cs`, `ExtensibleParser/Recovery/*.cs`.

---

## 0. Что изменилось относительно v1

Ядро v1 (итеративный ре-парсинг + модификация таблиц мемоизации) **сохраняется**: оно вытекает из архитектуры парсера и является его сильной стороной. Переработаны механизмы:

| # | Было в v1 | Стало в v2 | Почему |
|---|-----------|------------|--------|
| 1 | Реконструкция стека разбора из memo (`RecoveryStackReconstructor`) | **Живой `FailureSnapshot`** — снимок стека правил в момент самого дальнего mismatch | v1-реконструктор — эвристика: `GetDepth()` всегда 0 (ничего не переопределено, `ParseContext.cs:12`), связей родитель–дети нет, в «стек» попадают все failed-правила на позиции (десятки несвязанных правил) |
| 2 | `Partial` в отдельной таблице `_partialMemo` + `ContinueFromPartial`/`TryRecoverFromPartial`/`MergeWithPartialTree` | **`Partial` — обычный результат в `_memo`** + базовый порождающий случай в `ParseSeq` | В текущем коде `Result.Partial` **нигде не порождается** (все 13 мест в `Parser.cs:306,325,561,613,622,747,790,889,895,971,1003,1029,1038` — только пропагация). Весь v1-путь восстановления от partial — мёртвый код, на котором строилась Фаза 1 |
| 3 | Внешняя хэш-таблица `_skippedTextMap: TerminalNode → (pos, len)`, `IsRecovery` убран с узла | **`IsRecovery` остаётся на узле** (`SyntaxTree.cs:91,151`); пропущенный текст = узел, покрывающий `[E..S)` | Дерево должно быть самодостаточным (без привязки к инстансу `Parser`). `Dictionary<TerminalNode,…>` использует структурное равенство record, а не «reference equality» из текста v1. В Roslyn пропущенные токены — полноправные участники дерева (`SkippedTokensTrivia`), а не боковая таблица |
| 4 | `CleanMemoForRecovery`: удаляет `Failure` с `MaxFailPos == ErrorPos` и на `currentStartPos`, Success с `NewPos == ErrorPos`, Partial на `ErrorPos`, весь `_partialMemo`, `_terminalMemo` на `ErrorPos` | **Гигиена: удаляются только `Failure`-записи на точке восстановления E**; префикс `[0..E)` не трогается никогда | Правила очистки v1 удаляли только что применённые патчи (вставка терминала стоит на `ErrorPos`, модифицированный Partial заканчивается в `ErrorPos`) — стратегия «вставка токена» не работала бы в собственном цикле v1; полезная часть v1 (удаление `Failure` в точке ошибки) сохранена как Гигиена |
| 5 | Жадный выбор одного набора патчей (`SelectBestPatchSet`), при неудаче — fatal | **Ограниченный DFS с откатом**: патч принимается только если точка восстановления продвинулась (E2 > E), иначе откатывается и пробуют следующий кандидат | Жадный выбор без бэктрекинга: один плохой патч = падение всего парсинга, хотя более дорогой сработал бы |
| 6 | Инвариант прогресса «утверждается» + `MaxRecoveryAttemptsPerPosition` как «пластырь» | **Прогресс гарантирован по построению**: акцепт кандидата iff E2 > E; счётчики — только предохранитель | |
| 7 | Cost: вставка = 1, skip = `scanPos - pos` (символы) в 1.3, но «1 за токен» в 2.1 | **Единый cost**: вставка = 1; skip = число небелых «слов» + число переводов строк в пропущенном регионе (O(символов) оценка) | Смешанные единицы делали сравнение кандидатов бессмысленным |
| 8 | `RecoveryOptions.StopPredicate: Func<string,int,bool>` | **Только `Rule`/`Terminal`-данные** (`Anchors`/`CanStart` — правила) | Делегат ломает декларативность, value-equality и сериализуемость грамматик |
| 9 | Примеры с `new Alt([...])` | Убрано: типа `Alt` в `Rules.cs` нет (альтернативы — `Rule[]` в словаре) | Пример v1 не компилируется |
| 10 | `RecoveryPrefix`/`RecoveryPostfix` — «удаляются» | **Остаются и переиспользуются**: это единственный живой механизм recovery сегодня | Они работают (триггер `_recoverySkipPos`, `Parser.cs:284,504`); v2 переименовывает флаг в `_recoveryPoint` и сохраняет поведение |
| 11 | Триггер recovery: «ErrorPos продвинулся» | **Единый триггер**: результат не есть Success до EOF (включая Success дохавший до 80 из 100 — «хвостовой мусор») | Триггер v1 не покрывал хвостовой мусор (ErrorPos не двигается → мгновенный fatal): запланированный в v1 тест `Test_ParsingReachesEndOfString` (Фаза 5.2, не реализован) с ним был бы непротестирован |
| 12 | Точка resync ищется по follow-set (токен-уровень, гадание) | **Структурный resync**: позиция, где спекулятивный parse правила-якоря (`Statement`, `Member`) **успешен**, — known-good точка; мусор до неё либо абсорбируется, либо конструкция достраивается вставками (§3.4, S2) | Парсер уже умеет спекулятивно парсить за пределами точки ошибки (предикаты `&`/`!`): вместо гадания «где закрывающая» мы **знаем** «где начинается следующий валидный стейтмент/член» — дальше идёт валидный код |

---

## 1. Контекст: почему recovery устроен именно так

### 1.1. Свойства парсера, из которых всё следует

1. **Нондетерминизм + longest-match.** Парсер пробует все альтернативы и выбирает самую длинную (`Parser.cs:331-344`). Поэтому **падение отдельного правила — нормальное событие**, а не ошибка: продвижение может случиться в соседней альтернативе. Ошибка становится известной только на верхнем уровне: разбор не достиг конца строки (или завершился Partial-деревом с дырами).
2. **Нет лексера** — терминалы это regex-матчинг по тексту. Восстановление работает на уровне текста: аналог «BadToken» из лексера Roslyn у нас — **абсорбер-узел**, покрывающий произвольный регион текста.
3. **Нет EOF-токена** — парсер PEG-овый, в грамматике нет терминала «конец входа». «Добрали до конца» — это **проверка позиции**, а не матч терминала: `result.NewPos == input.Length` (единственный источник правды, `Parser.cs:181`). Для нужд follow-sets EOF **эмулируется** терминалом `EofTerminal` (`FollowSetCalculator.cs:14-18`): `TryMatch` возвращает `0` iff `position >= input.Length`, иначе `-1`. Сегодня это **приватный non-singleton** внутри `FollowSetCalculator` — engine и единый `TerminalComparer` (§3.3.3) не могут им пользоваться как есть. Правило: EOF выносится в **общий стабильный синглтон**, которым пользуются и `FollowSetCalculator`, и engine. Обращение с EOF в recovery: (а) «совпадение EOF» в S3/S4 — это проверка `pos == input.Length`, **не** regex; (б) EOF **никогда не вставляется** инъекцией — это позиция, а не токен; суффиксные обязательства completion stack — только реальные терминалы (EOF живёт в follow-sets, не в First-sets элементов суффикса); (в) S4 («достроение до конца») — resync-точка `S := input.Length`, валидная **по определению** (спекулятивного parse там нет — конец строки).
4. **Полная картина на момент ошибки.** К моменту, когда верхний уровень сообщает «не дошли до конца», у нас есть:
   - `ErrorPos` — самая дальняя позиция terminal-mismatch'а (протокол «самой дальней точки падения», `Parser.cs:804-812`);
   - `_memo` — для каждого `(pos, rule, prec)` результат: Success / Failure(maxFailPos);
   - **`FailureSnapshot`** (новый, §3.2) — точный стек правил с локациями в момент самого дальнего mismatch;
       - **Спекулятивная валидация** (§3.4, S2) — способность *проверить* любую позицию S ≥ E (включая саму точку ошибки — это чистые вставки без абсорбера): совпадает ли здесь правило грамматики (`Statement`, `Member`, …). Предикаты `&`/`!` дают парсеру механизм спекулятивного разбора; engine использует тот же механизм, чтобы **знать** точку resync, а не угадывать её: после разлома обычно идёт валидный код, и грамматика это докажет.

   Т.е. момент ошибки — это не «слепой» сбой, а состояние с полной диагностической информацией и возможностью проверять гипотезы о тексте. Это и есть смысл итеративного подхода.

5. **Мемоизация делает повторный проход почти бесплатным.** Префикс `[0..E)` воспроизводится из `_memo` за O(числа записей), а не за O(ввода). Модификация memo = «правка грамматики» (вставленный токен / пропущенный регион) с нулевой пересборкой префикса. Это и есть идея частичного разбора в memo: memo хранит не только результаты, но и точки, в которые можно «влезти», чтобы продолжить.

### 1.2. Почему не single-pass (инлайн-восстановление)

Инлайн-восстановление (как в Roslyn: recovery в момент встречи неожиданного токена) непереносимо на этот парсер по п. 1.1.1: в точке падения правила нет способа знать, что это ошибка — другая альтернатива может выиграть. Единственный момент, когда ошибка достоверна, — верхний уровень. Значит recovery — внешний к парсингу процесс: парсер честно падает, engine анализирует снимок, правит memo, парсер честно перезапускается.

Цена подхода: несколько проходов по вводу (каждый почти бесплатный за счёт memo) и необходимость строгой дисциплины мемоизации (инварианты §3.7). Цена оправдана: grammar-код и горячий путь парсинга не загрязняются recovery-логикой; вся логика восстановления — в одном месте, тестируемая отдельно.

### 1.3. Что заимствуем из Roslyn

По отчётам `C:\RSDN\ParserRecoveryStrategy.md` (39 стратегий парсера) и `C:\RSDN\LexerRecoveryStrategy.md` (21 стратегия лексера); ключевые механизмы верифицированы по исходникам (`c:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\`):

| Механизм Roslyn | Где (Roslyn) | Адаптация в CsNitra |
|---|---|---|
| **Missing token** — нулевой токен с ошибкой, полноправный узел дерева | `SyntaxParser.cs:521-568` (`EatToken`/`CreateMissingToken`) | Пустой `TerminalNode` (`ContentLength = 0`, `IsRecovery = true`) — патч вставки (S1, S3) |
| **Skipped tokens → trivia** на соседнем токене; **одна диагностика на цикл пропуска** (остальные — молча) | `SyntaxParser.cs:1018-1102` (`AddSkippedSyntax`); `LanguageParser.cs:4616-4641` | Абсорбер-узел `[E..S)` с `IsRecovery = true`; один `RecoveryDiagnostic` на skip (S2) |
| **Panic mode**: `SkipBadTokens*` — цикл «пока токен не ожидается: если terminator/abort — стоп, иначе съесть» | `LanguageParser.cs:4616-4667` и 11 контекстных обёрток | Стратегия S2: сканирование **текста** до первого терминатора из агрегированного follow-стека |
| **`TerminatorState`** — 29-флаговая маска условий остановки, ~100 ручных save/restore `_termState` по всему `LanguageParser.cs` (проверено: `LanguageParser.cs:57-137`, grep `_termState` → 100+ вхождений) | То же | **Декларативно**: `RecoveryOptions.Terminators` / `Anchors` на правиле (§3.9) + вычисленный follow-set как fallback. Это и есть «LL-идея» в чистом виде: контекстно-зависимые множества терминаторов, но заданные автором грамматики данными, а не размазанные по коду парсера |
| **Hand-written предикаты** `IsPossibleStatement`/`CanStartMember`/`IsNamespaceMemberStartOrStop` (в отчёте — «предикаты, не стратегии», `LanguageParser.cs:2427-2481, 11115-11183`) — токеновые эвристики «может ли здесь начаться X» | То же | **Правила грамматики как якоря**: спекулятивный parse правила `Statement`/`Member` в позиции S *доказывает* валидность resync-точки (T1, §3.4, S2); мягкие авторские `CanStart`-предикаты (T2) покрывают случай, когда следующий код тоже бит. Декларативно, в данных грамматики, без ручных предикатов |
| **`ConsumeUnexpectedTokens`** — хвостовой мусор до EOF съедается целиком, одна ошибка на узел | `LanguageParser.cs:14673-14688` | Стратегия S4: skip-до-EOF как последний резерв |
| **`IsMakingProgress`** — guard прогресса в циклах пропуска | `SyntaxParser.cs:1170-1189` | Инвариант I1 (§3.7): акцепт кандидата только при E2 > E |
| **`SkipBadMemberListTokens`** — подсчёт вложенности скобок при пропуске (`curlyCount`) | `LanguageParser.cs:2078-2152` | В S2: при сканировании до `}`/`)`/`]` учитываем вложенность пар (иначе найдём «чужую» закрывающую) |
| **`ParseWithStackGuard`** — защита от переполнения стека | `LanguageParser.cs:207-234` | Отложено (Фаза 3): `MaxRecoveryIterations` + лимиты глубины |

Важное наблюдение: специализированные «костыли» Roslyn (11 штук: `for→foreach`, misplaced `else`, переанализ namespace и т.д.) существуют потому, что в hand-written рекурсивном спуске recovery-правила нельзя выразить декларативно. У нас грамматика — данные (`Rule`-record'ы), поэтому аналог «костыля» — **правило в грамматике** (`Anchors`, `CanStart`, `RecoveryPrefix/Postfix`, `TryInsert`), а не код. Ядро recovery остаётся без hand-written стратегий.

---

## 2. Ключевые понятия

- **Точка восстановления E** — позиция, с которой начинается следующий проход:
  - результат Failure → `E = ErrorPos`;
  - результат Partial → `E = max(NewPos, ErrorPos)`;
  - результат Success с `NewPos < input.Length` → `E = NewPos` (хвостовой мусор).
- **Кандидат (набор патчей)** — минимальное изменение memo/инъекций, которое (а) делает возможными новые совпадения в точке E и (б) гарантирует продвижение или проверяется на продвижение.
- **Принятый патч** — кандидат, после применения которого ре-парсинг дал E2 > E. Принятые патчи **не откатываются** — они становятся частью «восстановленной грамматики».
- **Восстановленная грамматика** — исходная грамматика + принятые инъекции (вставленные токены, абсорберы). Каждый последующий проход парсит уже её.
- **Восстановленный парсинг** — финальный результат достиг EOF (Success или Partial). Partial при EOF — это «успех с дырами»: дерево полное (I4), дыры помечены `IsRecovery` и описаны в `RecoveryDiagnostics`.
- **Resync-якорь (known-good точка)** — позиция S ≥ E, в которой спекулятивный parse правила-якоря (`Statement`, `Member`, …) успешен с прогрессом (S == E — чистое достроение вставками, без пропуска): грамматика *доказывает*, что с S продолжается валидная структура. Якорь — знание, а не эвристика (§3.4, S2).
- **Completion stack** — для найденной resync-точки S вычисленный набор патчей («закрывающий» все разорванные конструкции снимка перед S): вставки суффиксных обязательств кадров + абсорбер мусора между E и S, если мусор есть (§3.4, S2).

---

## 3. Архитектура

### 3.1. Главный цикл

```csharp
public Result Parse(string input, string startRule, out int triviaLength, int startPos = 0)
{
    // инициализация: очистка _memo, _index, _injections, _lastSnapshot, _recoveryDiagnostics;
    // currentStartPos = startPos + лидирующий trivia (как в текущем Parse)
    var ePrev = -1;
    Result result = default;

    for (var iter = 0; ; iter++)
    {
        result = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);

        if (result.TryGetSuccess(out _, out var end) && end == input.Length)
            return result; // чистый успех

        var e = RecoveryPointOf(result, input);        // §2
        if (e <= ePrev)
            break;                                      // глобальный предохранитель (I3);
                                                        // по построению недостижим (E2 < E невозможен,
                                                        // префикс стабилен, I2) — оставляем как fail-safe
        ePrev = e;
        if (iter >= MaxRecoveryIterations)
            break;

        var snapshot = FailureSnapshotAt(e);           // §3.2 (для хвостового мусора — синтетический)
        _recoveryPoint = e;                            // триггер RecoveryPrefix/RecoveryPostfix (§3.4, S0)

        // S0 — неявный кандидат «ре-парсинг как есть» (§3.4, S0): генерируется самим циклом,
        // engine'ом никогда; Apply = только Hygiene, без патчей — RecoveryPrefix/Postfix/OftenMissed
        // срабатывают сами через _recoveryPoint. Всегда первый в списке. До построения engine'а
        // (Фаза 1.1) — единственный кандидат: легаси-поведение recovery сохраняется на переход.
        var candidates = new List<RecoveryCandidate> { CandidateS0(e, snapshot) };
        candidates.AddRange(_engine.Generate(e, snapshot, input)); // §3.4, детерминированно отсортированы
        var recoveredThisIteration = false;

        foreach (var candidate in candidates)
        {
            if (_attempts[e].Contains(candidate.Id))
                continue;
            _attempts[e].Add(candidate.Id);
            if (_attempts[e].Count > MaxRecoveryAttemptsPerPosition)
                break;

            var log = candidate.Apply(this);           // §3.5: патчи + Hygiene (удаление Failure на e) в одном атомарном шаге, всё в MemoPatch-логе для отката
            _recoveryPoint = e;
            var next = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
            var e2 = RecoveryPointOf(next, input);

            if (next.TryGetSuccess(out _, out var end2) && end2 == input.Length)
            {
                _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                return next;                           // полностью восстановлено
            }
            if (e2 > e)                                 // I1: прогресс — принимаем
            {
                _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                result = next;
                recoveredThisIteration = true;
                break;                                 // к следующему итеративному проходу
            }
            log.Rollback(this);                        // нет прогресса — откат (включая Hygiene), следующий кандидат
        }

        if (!recoveredThisIteration)
            break;                                      // кандидаты исчерпаны — ошибка неотвратима
    }

    // Финальная семантика §3.6: ErrorInfo = null только если результат достиг EOF
    // (Success@EOF — чистый успех, Partial@EOF — «восстановлено с дырами»);
    // Success < EOF / Failure / Partial < EOF — невосстановлено.
    var recovered = result.TryGetSuccess(out _, out var sEnd) && sEnd == input.Length
        || result.TryGetPartial(out _, out var pEnd) && pEnd == input.Length;
    ErrorInfo = recovered ? null : new FatalError(input, ErrorPos, ...);
    return result;
}
```

Свойства цикла:

- **Один проход = одна область ошибки.** После акцепта префикс `[0..e)` стабилен (I2), следующий проход фактически работает только в районе новой точки e2.
- **Легаси-путь = кандидат S0.** Неявный S0 (ре-парсинг как есть, Hygiene без патчей) воспроизводит текущее recovery-поведение (RecoveryPrefix/Postfix/OftenMissed через `_recoveryPoint`): до построения engine'а (Фаза 1.1) он единственный кандидат, после — просто кандидат с наивысшим приоритетом. Существующие recovery-тесты (Error-правила MiniC) не ломаются ни на одном этапе.
- **Ограниченность:** `MaxRecoveryAttemptsPerPosition` (default 3) × число точек ошибок ≤ `MaxRecoveryIterations` (default 64). Терминация гарантирована независимо от грамматики.
- **Детерминизм (I5):** порядок кандидатов задаётся полной сортировкой (§3.4.3); нигде не происходит выбора «первым попавшимся» по перебору `Dictionary`.

### 3.2. `FailureSnapshot` — живой снимок вместо реконструкции

Вместо `RecoveryStackReconstructor` (v1) снимок делается **в момент самого дальнего mismatch** — там, где ошибка происходит, а не восстанавливается задним числом.

**Кадр стека** (`Recovery/StackFrame.cs`):

```csharp
public readonly record struct StackFrame(
    string RuleName,      // имя правила (для Ref — имя референсируемого)
    int Precedence,       // minPrecedence вызова (TDOPP)
    FrameLocation Location,
    Terminal[]? Expected, // First-множество элемента, который парсится в этом кадре (null для кадров правил)
    RecoveryOptions? Options // аннотации правила, если оно обёрнуто в RecoveryRule
);

public abstract record FrameLocation;
public sealed record RuleFrameLocation(int AltIndex) : FrameLocation;      // альтернатива/префикс в правиле
public sealed record SeqFrameLocation(int ElementIndex) : FrameLocation;   // элемент Seq
public sealed record LoopFrameLocation(string LoopKind, int Iteration) : FrameLocation;
public sealed record PostfixFrameLocation(int PostfixIndex) : FrameLocation; // postfix TDOPP
```

**Дисциплина push/pop** (исправление текущей реализации, где `_ruleStack` пушится один раз в `ParseRule` — `Parser.cs:287` — с всегда-`null` локациями `Parser.cs:33`, и не чистится на раннем возврате `Parser.cs:347-351`):

- `ParseRule` — push `RuleFrameLocation(altIndex)` перед каждой альтернативой (включая prefix/postfix TDOPP);
- `ParseSeq` — push `SeqFrameLocation(i)` с `Expected = FirstSet(Elements[i])` перед парсингом i-го элемента;
- `ParseOneOrMany` / `ParseZeroOrMany` — push `LoopFrameLocation(kind, iteration)`;
- `TryParsePostfix` — push `PostfixFrameLocation(i)`;
- **все pop'ы — в `finally`** (текущий пропуск pop на пути `Parser.cs:347-351` устраняется).

Стоимость: push/pop — одна запись в `List<StackFrame>`; глубина = вложенность правил (десятки).

**Съёмка снимка** — в единственном месте, где двигается `ErrorPos` (`ParseTerminal`, §3.3):

```csharp
public sealed record FailureSnapshot(
    int Pos,
    StackFrame[] Stack,    // копия массива: start rule → … → внутренний кадр с упавшим элементом
    Terminal FailedTerminal,
    Terminal[] Expected    // Expected верхнего кадра + FailedTerminal
);
```

`Parser` хранит один `_lastSnapshot` (перезаписывается при каждом новом самом дальнем mismatch; истории нет). Спекулятивные парсинги снимок **не трогают** (§3.3.4).

Топ кадра = точный ответ на «что именно упало»: имя правила, локация (какой элемент/итерация/postfix), ожидаемые терминалы, прецедент вызова, аннотации. Это закрывает v1-проблемы «плоский список» и «неизвестен упавший элемент».

### 3.3. Терминальный кэш и инъекции

#### 3.3.1. Два слоя хранения

```csharp
// чистый кэш результата TryMatch (length >= 0 или -1); ключ — (pos, terminal) по TerminalComparer (§3.3.3)
private readonly Dictionary<(int Pos, Terminal Terminal), int> _terminalCache = new(TerminalComparer.KeyComparer);

// слой инъекций engine'а: проверяется ПЕРЕД _terminalCache
private readonly Dictionary<(int Pos, Terminal Terminal), Injection> _injections = new(TerminalComparer.KeyComparer);

public readonly record struct Injection(int Length, string NodeKind, bool IsSkip)
{
    public static Injection Insert(string kind) => new(0, kind, false);
    public static Injection Absorb(string kind, int length) => new(length, kind, true);
}
```

`TryMatch` — чистая функция `(terminal, input, pos)`, поэтому кэш mismatch'а (`-1`) **неправильен никогда и не чистится** (в отличие от v1, где `_terminalMemo` чистился на `ErrorPos` — это лишало кэш смысла и конфликтовало с инъекциями). Инъекция — отдельный слой поверх: «восстановленная грамматика» видит вставленный токен, истинный `TryMatch` при этом не переопределяется.

#### 3.3.2. `ParseTerminal` с сохранением семантики побочных эффектов

Протокол `ErrorPos`/`_expected` (`Parser.cs:804-812`) — единственный механизм, делающий `ErrorPos` осмысленным при longest-match, — **не может обходиться кэшем**. Факторизуем:

```csharp
private Result ParseTerminal(Terminal terminal, int startPos, string input)
{
    if (_injections.TryGetValue((startPos, terminal), out var inj))
        return CreateInjectedResult(terminal, inj, startPos, input);

    if (!_terminalCache.TryGetValue((startPos, terminal), out var contentLength))
    {
        contentLength = terminal.TryMatch(input, startPos);
        _terminalCache[(startPos, terminal)] = contentLength;
    }

    if (contentLength < 0)
    {
        ReportMismatch(terminal, startPos);
        return Result.Failure(startPos);
    }

    var currentPos = startPos + contentLength;
    var triviaLength = Trivia.TryMatch(input, currentPos);
    if (triviaLength > 0)
        currentPos += triviaLength;

    return Result.Success(
        new TerminalNode(terminal.Kind, startPos, currentPos, contentLength),
        currentPos, currentPos);
}

private void ReportMismatch(Terminal terminal, int pos)
{
    if (_suppressSideEffects)
        return;
    if (pos >= ErrorPos)
    {
        if (pos > ErrorPos)
        {
            _expected.Clear();
            ErrorPos = pos;
            CaptureSnapshot(pos, terminal);
        }
        _expected.Add(terminal);
    }
}
```

- Кэш пропускает только regex-матчинг; `ReportMismatch` вызывается всегда (и на cache hit, и на miss).
- `CreateInjectedResult`: `Length == 0` → пустой `TerminalNode(Injection.NodeKind, pos, pos, 0, IsRecovery: true)` (вставка); `Length > 0` → абсорбер `TerminalNode(NodeKind, pos, pos + Length, Length, IsRecovery: true)` (пропуск). `maxFailPos = pos + Length`.
- Убираем комментарий-вопрос `Parser.cs:834` (`maxFailPos: currentPos // ???`): для успешного терминала `maxFailPos = currentPos` — «дальше не заходили», корректно.

#### 3.3.3. Единая идентичность терминалов

Один компаратор везде (follow-sets, `_terminalCache`, `_injections`, `_expected`):

```csharp
public static class TerminalComparer
{
    // Literal — по Value; EOF/ε-терминалы — по общему синглтону (стабильная идентичность, §1.1 п.3);
    // остальные (сгенерированные терминалы — синглтоны) — по ссылке
    public static readonly IEqualityComparer<Terminal> Instance = ...;
    public static readonly IEqualityComparer<(int Pos, Terminal Terminal)> KeyComparer = ...;
}
```

(Сейчас в коде три схемы: record-equality в `Dictionary`-ключах, `TerminalEqualityComparer` в `FollowSetCalculator.cs:21-42`, «по инстансу» в тексте v1.) К тому же `EofTerminal`/`EmptyTerminal` — приватные **non-singleton**'ы (`FollowSetCalculator.cs:9-18`): «по ссылке» для них ломается, если engine или второй экземпляр калькулятора создадут собственные. Исправление (Фаза 0.4/0.5): вынести EOF/ε в **общие стабильные синглтоны** и пользоваться ими и в `FollowSetCalculator`, и в engine; компаратор сравнивает их по ссылке к синглтону.

#### 3.3.4. Изоляция спекулятивных парсингов

Все спекулятивные вызовы (предикаты `&`/`!`, speculative-проверка trailing separator в `ParseSeparatedList` — `Parser.cs:1018-1033`, pre-parse правил engine'ом §3.4) обёртываются в общий хелпер:

```csharp
private T Speculative<T>(Func<T> parse)
{
    // Побочные эффекты подавлены флагом: ReportMismatch/CaptureSnapshot проверяют
    // _suppressSideEffects (§3.3.2), поэтому ErrorPos/_expected/_lastSnapshot на время
    // спекуляции не двигаются. Сохранение/восстановление единственного _lastSnapshot
    // (без истории, §3.2) — двойная страховка.
    var savedErrorPos = ErrorPos;
    var savedExpected = _expected.ToArray();
    var savedSnapshot = _lastSnapshot;
    _suppressSideEffects = true;
    try
    {
        return parse();
    }
    finally
    {
        _suppressSideEffects = false;
        ErrorPos = savedErrorPos;
        _expected = new HashSet<Terminal>(savedExpected, TerminalComparer.Instance);
        _lastSnapshot = savedSnapshot;
    }
}
```

v1 (ЗН-11) изолировал только предикаты; та же класс бага — в speculative-проверке `SeparatedList` и в pre-parse engine'а. Общий хелпер закрывает все три.

### 3.4. Генерация кандидатов

Контекст: `e`, `FailureSnapshot`, `FollowSetCalculator`, `input`.

**Иерархия средств восстановления (принцип деградации).** Точные авторские средства (recovery-правила S0, `Anchors`, `CanStart`, `TryInsert`) > вычисленные из грамматики (выводимые якоря, follow/first-множества, completion stack) > грубые fallback'и (токен-терминаторы, EOF). Отсутствие точного средства **никогда не блокирует** восстановление — включается более грубое; наличие точного поднимает качество. Универсальный алгоритм расширяется пользовательскими правилами, помогающими восстановлению: если их нет — работают грубые механизмы, если есть — используются они.

Каждый кандидат — `RecoveryCandidate`:

```csharp
public sealed record RecoveryCandidate(
    string Id,                    // детерминированный: "S1:{rule}:{terminal}" / "S2:{rule}:{terminator}" ...
    int Rank,                     // стратегия S0..S5 (меньше = выше приоритет)
    int Pos,                      // точка применения (e или resync-точка S)
    int Cost,
    string RuleName,              // правило кадра (часть ключа сортировки, §3.4.3)
    string? TerminalKind,         // kind терминала (часть ключа сортировки, §3.4.3)
    Action<Parser> Apply,         // патчит _memo/_injections + Hygiene (через MemoPatch-лог, §3.5)
    Action<Parser> Rollback,
    RecoveryDiagnostic[] Diagnostics
);
```

Приоритеты стратегий (ранг в сортировке): **S0 (recovery-правила автора) < S1 (вставка ожидаемого) < S2 (resync к known-good точке: T1/T2) < S3 (токен-терминатор) < S4 (EOF) < S5 (хвостовой мусор)**.

#### S0. Пользовательские recovery-правила (самый высокий приоритет)

1. **TDOPP `RecoveryPrefix`/`RecoveryPostfix`** (живой механизм, `Parser.cs:79,84-97,284,504`): не генерируются engine'ом — срабатывают сами в ходе ре-парсинга (триггер `_recoveryPoint == startPos`). Если recovery-альтернатива совпала в точке E, ре-парсинг продвинет E2 и кандидат S0 «принят» без отдельного патча. Если не совпала — работают S1..S4.

   Кандидат S0 присутствует в **каждой** итерации — его генерирует сам цикл, не engine (см. §3.1): `Apply` = только Hygiene (без инъекций), `Rollback` восстанавливает удалённые `Failure`, диагностика пуста. До построения engine'а (Фаза 1.1) S0 — единственный кандидат: это и есть легаси-путь recovery, и он же гарантирует, что существующие recovery-тесты (Error-правила MiniC) зелёны на каждом этапе перехода.
2. **`RecoveryRule`-аннотации** (§3.9) на правилах кадра снимка (ближайший кадр wins по каждому полю):
    - `TryInsert` → кандидаты вставки (§3.4, S1) с рангом S0;
    - `Anchors`/`CanStart` → цели resync (S2) с рангом S0; `Terminators` → цели S3/S4.

Это декларативный аналог 11 hand-written «костылей» Roslyn: автор описывает recovery правилом/данными, не кодом.

#### S1. Вставка ожидаемого терминала (аналог Roslyn `EatToken` → `CreateMissingToken`)

Для каждого терминала `T` из (в порядке приоритета):
1. `FailedTerminal` снимка;
2. `Expected` верхнего кадра (First-множество упавшего элемента, §3.2);
3. `TryInsert` аннотаций (ранг S0);
4. `FollowSet(внутреннего правила снимка)`,

такого что `TryMatch(T, input, e) < 0` (если T реально совпадает — это не ошибка вставки):

- патч: `_injections[(e, T)] = Injection.Insert(T.Kind)`;
- cost = 1;
- диагностика: `Inserted` на `e`, «expected {T.Kind}, found {actual preview}».

**Как это работает в ре-парсинге:** `Failure`-запись упавшего правила на `e` удалена гигиеной (часть `Apply`, §3.1/§3.5) → правило переиспытывается → в точке `e` его первый терминал находит инъекцию (нулевой матч) → Seq продолжает со следующего элемента. Если следующая структура в точке `e` не сходится — правило снова падает на `e` → E2 == E → кандидат отклоняется, откат, следующий. Неверные вставки отбрасываются механикой, а не эвристиками.

#### S2. Resync к known-good точке — структурная валидация (знание, не гадание)

Вместо гадания «где ближайший терминатор» engine **проверяет** позиции начиная с точки ошибки (включая саму `e` — чистые вставки, без абсорбера) и дальше по тексту средствами грамматики. Опора: (а) после разлома обычно идёт валидный код — грамматика это докажет; (б) парсер уже умеет спекулятивно парсить (предикаты `&`/`!`, изоляция §3.3.4) — engine использует тот же механизм.

**Иерархия улик (tier)** — в каждой позиции S оценивается от сильной к слабой:

| Tier | Проверка | Что доказывает | Источник |
|---|---|---|---|
| **T1** | Полный спекулятивный parse **правила-якоря** успешен с прогрессом | «с S начинается *валидная* конструкция целиком» | `Anchors` (§3.9) ?? выводимые якоря: правила элементов циклов в кадрах снимка (`ZeroOrMany(Ref("Statement"))` → якорь `Statement`, `ZeroOrMany(Ref("Member"))` → `Member`) |
| **T2** | Спекулятивный parse **`CanStart`-предиката** успешен | «с S *может начаться* конструкция» — мягче полного правила, выше вероятность сработать | `CanStart` (§3.9), только авторские. Это и есть замена Roslyn-предикатов `IsPossibleStatement`/`CanStartMember`/`IsNamespaceMemberStartOrStop` — только декларативная, в данных грамматики |
| **T3** | Терминальный матч терминала из follow-множества кадров | «здесь может стоять терминатор» | S3 (отдельная стратегия, fallback) |
| **T4** | EOF | резерв | S4 |

**Почему T2 нужен отдельно от T1.** Код за текущим падением *сам может содержать ошибки*, и чистое правило грамматики (T1) на следующей конструкции может не разобраться — там T1 молчит, а мягкий `CanStart`-предикат (аналог `IsPossibleStatement`: «может ли здесь начаться стейтмент», без требования разобрать его целиком) сработает. Т2-точка безопасна по построению: кандидат принимается только при E2 > E (I1), поэтому даже если ре-парсинг с S снова упрётся в ошибку (следующий член тоже бит), прогресс есть — и следующая итерация восстановит и её. И наоборот, T1 — гарантия, что ре-парсинг с S разберётся минимум на всю якорную конструкцию.

**Поиск resync-точек:**

```
candidates = []
for S in e .. min(e + MaxSkip, input.Length):    // S == e — чистые вставки, абсорбера нет (completion stack, п.1)
    // T1: якоря (авторские + выводимые), в порядке объявления
    for anchor in AnchorsAt(snapshot):
        if FirstSet(anchor) не совпадает в S: continue      // дешёвый pre-filter
        if SpeculativeParse(anchor, S).ok and progress > 0:
            candidates += ResyncTo(S, T1, anchor)
            return candidates                               // ближайший доказанный — скан окончен
    // T2: CanStart-предикаты (авторские)
    for p in CanStartAt(snapshot):
        if FirstSet(p) не совпадает в S: continue
        if SpeculativeParse(p, S).ok:
            candidates += ResyncTo(S, T2, p)                // скан продолжается в поисках T1
```

`SpeculativeParse(rule, pos) → (ok, endPos)` — чистая функция (изолированно: без memo, без инъекций, без побочных эффектов, §3.3.4); результаты кэшируются на время `Parse()` в `Dictionary<(Rule, int), (bool, int)>` — позиции пересекаются между tier'ами, кандидатами и итерациями. Скан ограничен `MaxSkip` (аннотация, default 1000).

**Композиция патчей кандидата `ResyncTo(S, tier)` (completion stack):**

1. **Внутренний (упавший) кадр** (топ снимка):
   - S > e → **абсорбер** `[e..S)` для упавшего элемента: мусор между разрывом и known-good точкой сохраняется в дереве как «хвост» разорванной конструкции (I4 — ничего не теряется);
   - S == e → нулевая вставка First-терминала упавшего элемента (классический missing token).
2. **Внешние кадры** (по стеку снимка наружу, пока не достигнем start-правила): Seq-кадр, чей элемент i парсился в момент сбоя, вносит **суффиксное обязательство** — элементы i+1..end; из них не-nullable вносят свой First-терминал в стопку нулевых вставок **в точке S** (в порядке следования). Nullable-элементы (`Optional`, `ZeroOrMany`, `OftenMissed`, предикаты) пропускаются — конструкция может завершиться и так. Цикл-кадры и TDOPP-postfix-кадры обязательства не имеют (могут завершиться). Терминал, реально совпадающий в S, вставлять не нужно.

**Cost** = skip-cost([e..S)) + число вставок + tier-penalty (T1: 0, T2: 1). Ближайшая точка обычно побеждает длиной; при сопоставимых длинах побеждает доказанная (T1 дешевле T2). Одна диагностика `Skipped`/`Inserted` на регион (молча, как у Roslyn).

**Пример 1 — пропущенная `}` перед следующей функцией (T1):**

```
int foo() { int x
int bar() { return 0; }
```

Снимок на e (позиция `int bar`): `[Module(loop it.0) → Member → FunctionDecl(Seq 3=Block) → Block(Seq 1=stmts) → VarDecl(Seq 2=";")]`, FailedTerminal = `;`. Якорь `Member` (элемент цикла Module) спекулятивно совпадает в **S == e** (T1: `int bar() { return 0; }` разобран целиком). Completion stack: VarDecl (S == e) → вставить `;` в e; Block (суффикс после stmts = [`}`], не-nullable) → вставить `}` в e; остальные кадры — суффикс пуст. Патч: 2 вставки, cost 2, **нулевая потеря текста**. Ре-парсинг: `int x ;` → `}` → `int bar()` как новый Member → EOF.

Сравните с T3 (S3, токен-терминатор): он нашёл бы ближайший `}`-токен — закрывающую скобку тела *bar* — и абсорбировал `int bar() { return 0;` в мусор, сломав оба члена. T1 — разница между «восстановлено» и «спрятано».

**Пример 2 — двойная ошибка (T2):**

```
int foo() { int x
int bar( { return 0; }
```

T1 в S == e **не срабатывает**: полный `Member` на `int bar(` не разобёрётся (следующий член тоже бит). Авторский `CanStart: [new Ref("MemberStart")]` (мягкое правило: модификаторы/тип + ident, без требования к телу) срабатывает в той же точке → кандидат T2 (cost +1). Completion stack тот же: `;` и `}` в e. Ре-парсинг: `int x ;` → `}` → Module-цикл: `Member` на `int bar(` падает на `(` → новая точка E2 > e → **следующая итерация** восстанавливает bar (вставка `)`). Две итерации вместо одной — но обе ошибки восстановлены; без T2 на этой позиции engine откатился бы к грубым стратегиям (S3/S4) и, скорее всего, абсорбировал бы оба члена.

#### S3. Пропуск до токен-терминатора (fallback, panic mode, аналог Roslyn `SkipBadTokens*`)

Работает, когда ни T1, ни T2 не дали точки (или их кандидатов нет в пределах `MaxSkip`).

1. **Терминаторы** — упорядоченный агрегат по кадрам снимка (внутренние правила первыми — «ближайшая скоба важнее дальней», как у Roslyn с `_termState`):
   `Terminators(snapshot) =` для каждого кадра от внутреннего к внешнему: `Options.Terminators ?? FollowSet(rule)`; дедупликация с сохранением порядка. (First-терминалы якорей/CanStart в S2 уже учитывались как T3-level pre-filter.)
2. **Сканирование** от `e+1` (skip нулевой длины — это вставка, её ведёт S1) до первого `S`, где совпал терминатор, с ограничением `MaxSkip`. При сканировании до `}`/`)`/`]` — подсчёт вложенности пар (аналог `curlyCount` в `SkipBadMemberListTokens`): «чужая» закрывающая внутри региона не останавливает скан.
    Матчинг терминаторов — через общий кэш `(pos, terminal) → len` (один на все сканы внутри вызова engine'а; позиции пересекаются между кандидатами). Терминатор-EOF (`EofTerminal` из follow-сета стартового правила) «совпадает» только при `S == input.Length` (проверка позиции, **не** regex, §1.1 п.3) — это естественная граница скана `min(e + MaxSkip, input.Length)`; если до конца входа не найден ни один реальный терминатор, кандидат S3 с EOF-целью совпадает с S4.
3. **Патч** зависит от типа упавшего элемента (топ кадра снимка):
   - элемент — `Ref(R)` → `_memo[(e, R, prec)] = Result.Success(AbsorberNode(e..S), S, S)` для всех `(e, R, prec')`, присутствующих в memo (прецеденты TDOPP — v1-пробел закрыт);
   - элемент — `Terminal T` → `_injections[(e, T)] = Injection.Absorb(T.Kind, S - e)`.
4. **Cost** = оценка числа токенов в `[e..S)`: число небелых «слов» (максимальных небелых прогонов) + число переводов строк (перенос строки = граница стейтмента — хуже, как в эвристике Roslyn `skipToNextLine`). Одна диагностика `Skipped` на регион (остальной регион — молча, как у Roslyn).
5. Если терминатор не найден до `MaxSkip` → кандидатов S3 нет; конец строки покрывает S4.

#### S4. Достроение в конце строки (E == EOF) — частный случай S2

То же completion stack, что в S2, но `S := input.Length` (EOF — это **позиция**, не токен; валидная точка **по определению**, tier T4 — спекулятивного parse там нет, конец строки, §1.1 п.3). Классическое «пропущенные закрывающие»: суффиксные обязательства всех открытых кадров, вставляемые **внутренние наружу** (сначала `}` внутреннего блока, потом внешнего — порядок важен):

- для каждого кадра: First-терминал суффикса `T` (всегда реальный терминал — EOF в First-sets не бывает), который не совпадает в `input.Length` (`T.TryMatch(input, input.Length) < 0`; для реальных терминалов это всегда так) → `_injections[(e, T)] = Injection.Insert(T.Kind)`;
- EOF **не вставляется** (инъекция — только реальные токены);
- cost = число вставок; диагностика на каждую.

#### S5. Хвостовой мусор (результат Success, E < EOF)

Аналог Roslyn `ConsumeUnexpectedTokens`: один абсорбер на `[e..input.Length)` (kind `Trailing`), cost = оценка токенов + 1. Синтетический снимок: один кадр start-правила с `Expected = [EOF]`.

#### 3.4.3. Детерминированный порядок

`candidates.OrderBy(c => (c.Rank, c.Cost, c.Pos, c.RuleName, c.TerminalKind))`. Никакого выбора по порядку перебора словарей (I5): повторный запуск на том же вводе даёт тот же порядок проб. Сканирование S2 детерминировано: позиции по возрастанию, якоря/CanStart в порядке объявления в аннотации, tier — от сильного к слабому; кэш спекулятивных результатов не влияет на порядок.

### 3.5. Применение, откат, гигиена memo

**Патч-лог** (основа отката):

```csharp
public readonly record struct MemoPatch(
    object Table,                 // ссылка на _memo / _injections (для ключей)
    object Key,
    object? OldValue,             // предыдущее значение (или null)
    object NewValue
);
// Apply: записать, запомнив OldValue; Rollback: вернуть OldValue (или удалить)
```

**Гигиена** (заменяет v1 `CleanMemoForRecovery` и текущую чистку `Parser.cs:225-239`):

```csharp
private void Hygiene(int e, FailureSnapshot snapshot)
{
    // удаляем ТОЛЬКО Failure-записи на точке e для правил из снимка (все их прецеденты)
    // — чтобы правила переиспытались с учётом новых инъекций.
    // Префиксные записи (pos < e) и Success/Partial записи не трогаем никогда (I2):
    // Success, заканчивающийся в e, — валидный факт префикса, а не «часть пути к ошибке».
    foreach (var ruleName in snapshot.Stack.Select(f => f.RuleName).Distinct())
        foreach (var prec in _index.GetPrecedences(e, ruleName))
            if (_memo.TryGetValue((e, ruleName, prec), out var r) && r.ResultKind == Result.Kind.Failure)
                _memo.Remove((e, ruleName, prec));
}
```

Обратный индекс `_index: Dictionary<int, HashSet<(string rule, int prec)>>` (позиция → ключи memo) ведется при каждой записи в `_memo`/удалении — hygiene за O(числа правил в снимке), а не O(таблицы) как в v1.

**Почему v1-чистка была ошибочной:** её правила (`Failure` на `MaxFailPos == ErrorPos` и `currentStartPos`, `Success` с `NewPos == ErrorPos`, `Partial` на `ErrorPos`, весь `_partialMemo`, `_terminalMemo` на `ErrorPos`) смешивали необходимое (удаление `Failure` в точке ошибки — иначе правила не переиспытываются) с вредным: `Success` вставленного терминала и модифицированный Partial заканчиваются ровно в `ErrorPos`, то есть чистка удаляла применённые в той же итерации патчи. В v2 принимаемый патч по построению не удаляется: инъекции живут до конца парсинга, а из `_memo` на `e` удаляются только `Failure`, которые патч и призван переопределить.

### 3.6. `Partial` как первоклассный результат «сбой с прогрессом»

Текущая проблема: `Result.Partial` не порождается нигде (все создания — пропагация), поэтому `_partialMemo`, `_partialAccumulated`, `ContinueFromPartial` и всё, что на них стоит, — мёртвый код. Исправляем базовым случаем.

**Базовый случай — в `ParseSeq`** (`Parser.cs:838-897`): элемент `i > 0` не совпал (не success, не partial), но прогресс есть →

```csharp
if (!gotSuccess && !gotPartial)
{
    if (elemIdx > 0)
        return Result.Partial(
            node: BuildPartialSeqNode(seq, elements, startPos, newPos),
            newPos,
            result.MaxFailPos,
            new ParseContext(seq.Kind, new SeqFrameLocation(elemIdx), FirstSet.Get(seq.Elements[elemIdx]), null));
    return result; // прогресса нет — обычный Failure
}
```

`FirstSet.Get(rule)` — чистая функция над деревом правила (`Recovery/FirstSets.cs`): `Terminal → {t}`; `Seq →` first-цепочка с nullable-сходом; циклы/`Optional`/`OftenMissed` → first(element) ∪ {ε}; предикаты → ∅ (nullable); `Ref`/`ReqRef` → first-множество правила из `FollowSetCalculator`. Заменяет хрупкий `GetExpectedTerminals` (`Parser.cs:899-912`), который всегда возвращал терминалы **первого** элемента.

Поле `Options` в `ParseContext` в базовом случае — `null`: `RecoveryRule`-аннотации живут на уровне правила (обёртка вокруг альтернативы), а `ParseSeq` к ним доступа не имеет; engine читает опции из кадров снимка (`StackFrame.Options`, §3.2/§3.9).

Циклы (`OneOrMany`/`ZeroOrMany`/`SeparatedList`) базового случая **не получают**: «элемент не совпал → цикл завершился» — нормальная семантика повторения; ошибка проявится там, где после цикла ожидалось продолжение (напр., `}` после стейтментов).

**Тай-брейк longest-match** (дополнение к `Parser.cs:331-344`):

- Success и Partial одинаковой длины → **Success выигрывает** (чистый разбор лучше дырчатого; локальное правило, заменяет v1 глобальный `CleanPreferenceThreshold`);
- Partial большей длины чем Success → Partial выигрывает (longest-match доминирует);
- текущий хак `Parser.cs:336-343` (`bestResult == null && isRecoveryPos`) удаляется: его случай (Error-правила при равной длине) покрывается S0-приоритетом.

**Финальные состояния `Parse`** (семантика, которой не было ни в коде, ни в v1):

| Итог | Значение |
|---|---|
| Success, `NewPos == EOF` | чистый успех, `ErrorInfo = null` |
| Partial, `NewPos == EOF` | **восстановлено с дырами**: дерево полное (I4), `ErrorInfo = null`, `RecoveryDiagnostics` описывает дыры |
| Success, `NewPos < EOF` / Failure / Partial `< EOF` после исчерпания кандидатов | **невосстановлено**: `ErrorInfo = FatalError` (последняя неотвратимая точка), `RecoveryDiagnostics` — все принятые восстановления до неё |

### 3.7. Инварианты

- **I1 (прогресс).** Принятый кандидат строго сдвигает точку восстановления: E2 > E. Проверяется при акцепте; кандидат без прогресса откатывается.
- **I2 (стабильность префикса).** Записи `_memo`/`_terminalCache` с `pos < E` после создания не модифицируются и не удаляются (кроме rollback'а отклонённого кандидата, который и не трогал префикс). Следствие: префикс воспроизводится идентично каждый проход, и E2 < E невозможен (самый дальний mismatch не может «откатиться» влево — всё слева от E детерминировано).
- **I3 (терминация).** `MaxRecoveryAttemptsPerPosition` (default 3) на позицию + `MaxRecoveryIterations` (default 64) глобально.
- **I4 (ничего не теряется).** Каждый символ входа входит ровно в один терминальный узел финального дерева (обычный, вставленный или абсорбер). Тестируемо: `Test_EverythingRepresentedInTree`.
- **I5 (детерминизм).** Один вход + одна грамматика → одинаковое дерево и одинаковые диагностики; порядок кандидатов — полная сортировка (§3.4.3).
- **I6 (безвредность).** На корректном коде (Success до EOF без recovery) дерево в точности совпадает с до-recovery (recovery латентен); regressions-тесты на всех существующих грамматиках (JSON, Cpp, DOT, CsNitra).

### 3.8. Дерево и диагностика

- `IsRecovery` **остаётся** на `Node`/`TerminalNode` (`SyntaxTree.cs:91,151`) — дерево самодостаточное: visitor получает его без `Parser`-инстанса. Вставленный токен: пустой `TerminalNode(…, ContentLength: 0, IsRecovery: true)`. Пропущенный текст: `TerminalNode(…, [e..S), S-e, IsRecovery: true)` — текст читается visitor'ом из `input` по позициям (дерево и сейчас хранит только позиции, `Parser.Input` — debug-only, `Parser.cs:19-21`).
- v1-идея внешнего `_skippedTextMap` **отклоняется** (см. §0, п.3).
- Диагностика:

```csharp
public enum RecoveryKind { Inserted, Skipped, Unrecovered }

public sealed record RecoveryDiagnostic(
    int StartPos,
    int EndPos,
    RecoveryKind Kind,
    string Message,
    Terminal? Terminal,
    string? RuleName
);

public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics { get; } // накапливается за все итерации
```

- `ErrorInfo` (единичный `FatalError`, `Parser.cs:26`) — только для невосстановленного результата (§3.6). На восстановленном с дырами — `null`.

### 3.9. Аннотации автора грамматики

Замещают `TerminatorState` Roslyn (29 флагов × ~100 ручных save/restore) декларативными данными грамматики:

```csharp
public sealed record RecoveryRule(Rule Inner, RecoveryOptions Options) : Rule(Inner.Kind)
{
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new RecoveryRule(Inner.InlineReferences(inlineableRules), Options);
    // GetSubRules/ToString — делегирование Inner + вывод опций
}

public sealed record RecoveryOptions
{
    /// Явные терминаторы (переопределяют вычисленный follow-set): цели skip (S3) и достроения (S4).
    public Terminal[]? Terminators { get; init; }

    /// Правила-якоря для структурного resync (S2, T1): позиция, где такое правило
    /// проходит полную спекулятивную валидацию, — known-good точка продолжения.
    /// Обычно это основные правила грамматики (Statement, Member).
    /// Если не заданы — engine выводит якоря из элементов циклов в кадрах снимка.
    public Rule[]? Anchors { get; init; }

    /// Мягкие предикаты «может начаться» (S2, T2) — декларативный аналог
    /// Roslyn IsPossibleStatement/CanStartMember/IsNamespaceMemberStartOrStop.
    /// Допустимо грубее полного правила: отвечают «может ли здесь начаться конструкция»
    /// без требования разобрать её целиком. Критичны, когда код после разлома
    /// тоже содержит ошибки и полное правило (T1) не срабатывает.
    /// Никогда не выводятся автоматически — только авторские.
    public Rule[]? CanStart { get; init; }

    /// Терминалы, которые engine может вставлять при сбое этого правила.
    public Terminal[]? TryInsert { get; init; }

    /// Лимит resync-сканирования в символах (default 1000).
    public int? MaxSkip { get; init; }

    /// Opt-out: правило не восстанавливается (строгие контексты).
    public bool Recoverable { get; init; } = true;
}
```

Правила:

- **Нет делегатов** — только `Rule`/`Terminal`-данные: record сохраняет value-equality, грамматика остаётся сериализуемой и диффуемой, совместимой с динамическим расширением грамматик (фишка фреймворка).
- `ParseAlternative` разворачивает `RecoveryRule` (case рядом с `OftenMissed`); опции доступны engine'у через кадр снимка (`StackFrame.Options`) и через `Rules`-словарь для `Ref`.
- Приоритет: опции **ближайшего** кадра к упавшему элементу; поле, заданное в ближайшем кадре, переопределяет вычисленное (follow-set — только fallback). Конфликт «автоматика vs автор» решён в пользу автора — без v1-макханжества «комбинируем неконфликтующие»: авторские цели либо используются, либо нет.
- **Якоря выводятся**: если `Anchors` не заданы, engine использует правила элементов циклов из кадров снимка (`ZeroOrMany(Ref("Statement"))` → якорь `Statement`); авторские `Anchors` расширяют/переопределяют выводимые. `CanStart` не выводится никогда — его семантика («мягкий предикат») не выводима из грамматики.
- `OftenMissed` (`Rules.cs:182`) остаётся сахаром: эквивалент `RecoveryRule(element, new RecoveryOptions(TryInsert: [terminal]))` (на практике `element` — `Literal`, напр. MiniC). Текущее срабатывание только в recovery-позиции (`startPos == _recoverySkipPos`, `Parser.cs:688`) сохраняется: `TryInsert`-кандидаты генерируются в точке восстановления `e` — поведение совместимо (Фаза 2.3).
- Пример (MiniC):

```csharp
Rules["Statement"] =
[
    new RecoveryRule(
        new Seq([new Literal("int"), Terminals.Ident(), new Literal(";")], "VarDecl"),
        new RecoveryOptions(
            TryInsert: [new Literal(";")],
            Terminators: [new Literal("}"), new Literal("else")])),
    // ...
];

Rules["Block"] =
[
    new RecoveryRule(
        new Seq([new Literal("{"), new ZeroOrMany(new Ref("Statement")), new Literal("}")], "MultiBlock"),
        new RecoveryOptions(
            Terminators: [new Literal("}")],
            Anchors: [new Ref("Statement")] /* resync к следующему валидному стейтменту (T1) */)),
];

// Top-level: якорь Member выводится автоматически из цикла Module (ZeroOrMany(Ref("Member"))),
// а мягкий CanStart-предикат поднимает вероятность resync, если следующий член тоже бит (T2):
Rules["Module"] =
[
    new RecoveryRule(
        new ZeroOrMany(new Ref("Member")),
        new RecoveryOptions(
            CanStart: [new Ref("MemberStart")] /* мягкое: модификаторы/тип + ident, без требования к телу */)),
];
```

### 3.10. `FollowSetCalculator` — исправления

1. **Вложенные циклы.** Текущий «дополнительный проход» (`ProcessFollowSetForLoops*`, `FollowSetCalculator.cs:296-447`) не рекурсирует в тело цикла в поиске вложенных циклов (`ZeroOrMany(Seq([ZeroOrMany(Ref)]))` — внутренний цикл не обрабатывается). Чек-лист v1 отмечает 0.1 «done», но общий случай не покрыт. Исправление: единый обход дерева правила с накоплением «что следует после» (first/nullable suffix), без двух независимых проходов.
2. **`GetTerminators(IReadOnlyList<StackFrame> stack)`** — упорядоченная агрегация по §3.4, S3 (аннотации кадра ?? follow(правила кадра), дедупликация, EOF в конец). EOF здесь — **общий стабильный `EofTerminal`-синглтон** (эмуляция конца входа, §1.1 п.3), а не приватный non-singleton калькулятора.
3. Единый `TerminalComparer` (§3.3.3) + вынос `EofTerminal`/`EmptyTerminal` из приватных non-singleton'ов в общие стабильные синглтоны.
4. Настроить `Test_FollowSet_NestedRules` на формы: цикл в теле цикла, цикл внутри Seq внутри тела цикла.

---

## 4. Фазы

Каждая фаза — отдельный коммит с тестами; фаза N зависит только от N-1. На границе фаз система целиком работоспособна: до Фазы 1.1 recovery ведёт себя как сейчас (неявный S0-кандидат = текущий легаси-путь).

### Фаза 0. Фундамент парсера (исправления кода, нового recovery-поведения нет)

Цель: починить базу, на которой строится engine. После Фазы 0 recovery-поведение парсера **не меняется** на корректных и на текущих некорректных входах (кроме устранения зацикливания), но `Partial` и снимки становятся реальными.

| # | Что | Файлы | Тесты |
|---|-----|-------|-------|
| 0.1 | Дисциплина `_ruleStack` → `List<StackFrame>`: push с локациями (Seq/Loop/Postfix/Alt + `Expected` + `Precedence` + `Options`), pop в `finally` | `Parser.cs`, `Recovery/StackFrame.cs` | `StackFrameTests`: глубина/локации/очистка при исключениях |
| 0.2 | `FailureSnapshot`: `CaptureSnapshot` в `ReportMismatch`, `_lastSnapshot`, `Speculative`-хелпер, изоляция предикатов + speculative-проверки `SeparatedList` | `Parser.cs`, `Recovery/FailureSnapshot.cs` | `SnapshotTests`: снимок при самом дальнем mismatch; спекуляция не портит снимок/`ErrorPos`/`_expected` |
| 0.3 | Базовый случай `Partial` в `ParseSeq` + `FirstSets.Get` + тай-брейк (Success бьёт Partial при равной длине) + guard нуля в `OneOrMany`/`ZeroOrMany` (break при `newPos == currentPos` — устраняет бесконечный цикл на ε-элементе, напр. `ZeroOrMany(OftenMissed(…))`) + удаление хака `Parser.cs:336-343` | `Parser.cs`, `Recovery/FirstSets.cs` | `PartialBaseCaseTests`: `int x int y;` даёт Partial с `SeqFrameLocation(2)`; ε-цикл не виснет; тай-брейки |
| 0.4 | Терминальный кэш (чистый) + слой инъекций + `ReportMismatch`; единый `TerminalComparer` (+ общий стабильный `EofTerminal`/`EmptyTerminal`-синглтон вместо приватных non-singleton'ов `FollowSetCalculator.cs:9-18`) | `Parser.cs`, `Recovery/TerminalComparer.cs`, `Recovery/Injection.cs`, `FollowSetCalculator.cs` | `TerminalCacheTests`: кэш mismatch'а стабилен; инъекция поверх кэша; идентичность `Literal` vs instance; EOF-синглтон идентичен между калькулятором и engine |
| 0.5 | `FollowSetCalculator`: вложенные циклы, `GetTerminators(stack)` (EOF — общий синглтон, §3.10), компаратор | `FollowSetCalculator.cs` | `FollowSetTests` (расширить v1-набор: цикл в теле цикла; follow стартового правила содержит EOF-синглтон) |
| 0.6 | `RecoveryDiagnostic` + `Parser.RecoveryDiagnostics` (пустой список в Фазе 0) | `Parser.cs`, `Recovery/RecoveryDiagnostic.cs` | — |
| 0.7 | **Удаление мёртвого кода**: `ContinueFromPartial`, `TryRecoverFromPartial`, `MergeWithPartialTree`, `ContinueFromPartialPostfixFallback`, поле `_partialAccumulated`, таблица `_partialMemo`, `RecoveryStackReconstructor.cs` (+ его тесты), ветки `_partialMemo` в `ParseRule` (`Parser.cs:269-275,347-351`) и в `Parse` (`Parser.cs:209-223`); переименование `_recoverySkipPos` → `_recoveryPoint` (поведание RecoveryPrefix/Postfix сохраняется) | `Parser.cs`, `Recovery/`, `Tests/` | Регрессия: все существующие тесты ParserTests без изменений |

**Регрессия Фазы 0 (I6):** снапшот-тесты дерева на всех грамматиках репо (JSON, CppSimplified, DOT, CsNitra) — дерево на корректных входах не меняется.

### Фаза 1. Recovery engine (итеративный цикл)

| # | Что | Файлы | Тесты |
|---|-----|-------|-------|
| 1.1 | Главный цикл §3.1: `RecoveryPointOf`, `FailureSnapshotAt`, `_recoveryPoint`, **неявный S0-кандидат** (ре-парсинг как есть: Hygiene без патчей — RecoveryPrefix/Postfix/OftenMissed срабатывают сами; до появления engine'а — единственный кандидат), счётчики, fail-safe `ePrev` | `Parser.cs` | `IterativeRecoveryTests`: один проход на одну ошибку; предельные случаи (максимум итераций); **существующие recovery-тесты MiniC (Error-правила) зелёны без изменений** |
| 1.2 | `RecoveryEngine.Generate`: S1 (вставка), **S2 (resync: T1 якоря / T2 CanStart, pre-filter по First-множеству, спекулятивная валидация с кэшем `(rule, pos)`, completion stack)**, S3 (токен-скан со вложенностью пар, кэшем, MaxSkip), S4 (EOF), S5 (хвост); детерминированная сортировка | `Recovery/RecoveryEngine.cs`, `Recovery/RecoveryCandidate.cs` | `CandidateGenerationTests` (per-стратегия + порядок + детерминизм: два прогона → один порядок); `AnchorResyncTests`: T1 находит следующий Member/Statement; T2 (CanStart) на двойной ошибке; completion stack вставляет `;`+`}` (пример 1, §3.4 S2); мусор между e и якорем → абсорбер |
| 1.3 | Применение/откат (`MemoPatch`-лог), акцепт при E2 > E, `Hygiene` с обратным индексом `_index` | `Recovery/MemoPatch.cs`, `Parser.cs` | `PatchRollbackTests`: откат восстанавливает memo побайтово; префикс не тронут (I2); hygiene удаляет только Failure на e |
| 1.4 | Семантика финала §3.6: `ErrorInfo`/`RecoveryDiagnostics`, Partial-при-EOF как восстановленное | `Parser.cs` | `FinalStateTests` |
| 1.5 | `RecoveryRule`-развёртка в `ParseAlternative` (поведение в точности = `Inner`, только чтение аннотаций, без `RecoveryOptions`-полных — они в Фазе 2); S0-кандидат в единой детерминированной сортировке | `Parser.cs` | `S0IntegrationTests`: MiniC с Error-правилами; `RecoveryRuleTests.ParsesLikeInner` |

**Интеграционные тесты Фазы 1:** пропущенная `;`, пропущенная `}` (вкл. вложенные), неожиданный токен в выражении, хвостовой мусор, несколько ошибок (проходы 1→2→3), корректный код без recovery-узлов (I6), «всё в дереве» (I4), детерминизм (I5).

### Фаза 2. Аннотации автора + качество

| # | Что | Файлы | Тесты |
|---|-----|-------|-------|
| 2.1 | `RecoveryRule`/`RecoveryOptions` (§3.9) в `Rules.cs`; развёртка в `ParseAlternative`; engine читает опции из кадров (ближайший wins); вывод якорей из циклов | `Rules.cs`, `Parser.cs`, `RecoveryEngine.cs` | `RecoveryRuleTests`: `Terminators` переопределяет follow; `Anchors` (авторские + выводимые, T1); `CanStart` (T2); `TryInsert`; `MaxSkip`; `Recoverable=false` |
| 2.2 | Единый cost model §3.4 (вставка 1; skip = слова+переносы; tier-penalty T1: 0, T2: 1; S4 = число вставок); `CountRecoveryNodes` (обход по `IsRecovery` — теперь поле узла, cheap) для отладки/метрик | `Recovery/CostCalculator.cs` | `CostModelTests` |
| 2.3 | `OftenMissed` → документированный сахар над `TryInsert`; поведенческая совместимость | `Rules.cs`, `Parser.cs` | `OftenMissedTests` (расширить v1) |
| 2.4 | Производительность: обратный индекс уже в 1.3; бенчмарк «N ошибок → число проходов, время, размер memo»; оптимизация скана (дешёвые терминаторы первыми — single-char literals) | `Tests/ParserTests` (benchmark) | `RecoveryPerfTests` |

### Фаза 3. End-to-end и документация

| # | Что |
|---|-----|
| 3.1 | E2E-набор на MiniC: `Test_MissingClosingBrace_Function`, `Test_MissingSemicolon_MultipleStatements`, `Test_UnexpectedToken_Expression`, `Test_NestedErrors_MultipleBlocks`, `Test_TrailingGarbage`, `Test_Recovery_DoesNotBreakCorrectCode` (I6), `Test_EverythingRepresentedInTree` (I4), `Test_ParsingReachesEndOfString`, `Test_Deterministic` (I5), `Test_Anchor_Resync_NextMember` (T1), `Test_DoubleError_CanStart` (T2) |
| 3.2 | Документация: (а) гайд автора грамматики — когда что аннотировать, таблица соответствия `TerminatorState`-флагов Roslyn → `RecoveryOptions`; (б) семантика восстановления — как выглядит дерево/диагностика, таблица финальных состояний §3.6; (в) известные ограничения (качество follow-set на regex-терминалах, cost-оценки — эвристики) |
| 3.3 | Опционально: `ParseWithStackGuard`-аналог (защита от stack overflow на патологической вложенности), `RecoveryEnabled=false` (полное отключение → однопассовый режим) |
| 3.4 | Обновить `RecoverySystemChecklist.md` по Фазам v2; пометить `RecoverySystemPlan.md` как superseded |

---

## 5. Риски и митигация

| Риск | Влияние | Митигация |
|---|---|---|
| Базовый случай `Partial` меняет разбор **некорректных** входов (Partial пропагирует выше, внешние Seq продолжают после частичного элемента) | Дерево на битом коде меняется | Это и есть цель («ничего не теряется»); на корректном коде изменений нет (I6, регрессия Фазы 0 на всех грамматиках) |
| Quality follow-set на regex-терминалах (First терминала = он сам; regex матчит множество форм) | Неточные цели skip | `RecoveryOptions.Terminators` — переопределение автором; скан ограниченный (MaxSkip); неверный skip отклоняется механикой I1 (E2 не продвинулся → откат) |
| Цена скана skip (regex на каждой позиции) | O(MaxSkip × \|терминаторов\| × regex) на ошибку | Общий кэш `(pos, terminal)`; дешёвые терминаторы первыми; MaxSkip (default 1000); на практике терминаторы — короткие literals |
| Взрыв числа кандидатов | Время на одной ошибке | `MaxRecoveryAttemptsPerPosition` (3) — отклонённые кандидаты не повторяются; генерация ограничена кадрами снимка (не всей таблицей) |
| Цена спекулятивной валидации T1/T2 (полный parse правила в позициях скана) | O(MaxSkip × parse) | Два уровня: дешёвый pre-filter по First-множеству (терминальный матч) → полная валидация только в позициях-кандидатах; кэш `(rule, pos) → (ok, endPos)` на время `Parse()`; скан останавливается на первом T1; `MaxSkip` |
| Взаимодействие с динамическим расширением грамматик (правила меняются во время парсинга) | Memo на именах правил устаревает | Инвариант: **грамматика замораживается на время `Parse`** (задокументировать в API); динамическое расширение — между `Parse` |
| Патч `Ref(R)` на (e, R, \*) затрагивает и другие пути, пришедшие в (e, R) | Побочное влияние на соседние альтернативы | Это осознанно: «восстановленная грамматика» применяется ко всему ре-парсингу; неверное влияние обнаруживается I1 (нет прогресса → откат) |
| Патологический ввод (вложенность 10⁵) | Stack overflow рекурсии парсера | Отложено в 3.3 (guard как у Roslyn `ParseWithStackGuard`); лимиты итераций уже ограничивают recovery-цикл |

---

## 6. Справочник: текущие механизмы и их судьба

| Код | Статус | Судьба в v2 |
|---|---|---|
| Цикл `for(;;)` в `Parse` (`Parser.cs:174-241`) | жив | Переписывается (§3.1), структура сохраняется |
| `_recoverySkipPos` (`Parser.cs:28,158,178,206,249,254,256,405,409,420,432,442,503,546,548,688`) | жив | → `_recoveryPoint` (переименование, поведение RecoveryPrefix/Postfix сохраняется) |
| `RecoveryPrefix`/`RecoveryPostfix` в `TdoppRule` (`Rules.cs:292-299`, `Parser.cs:79-107`) | жив | Остаются, становятся S0 (приоритетом) |
| `OftenMissed` (`Rules.cs:182`, `Parser.cs:684-691`) | жив | Остаётся, сахар над `TryInsert` (2.3) |
| `RecoveryTerminal`/`EmptyTerminal` (`Rules.cs:445-457`) | жив (частично) | Остаются; `EmptyTerminal` — ручной вариант инъекции |
| `ContinueFromPartial`/`TryRecoverFromPartial`/`MergeWithPartialTree`/`ContinueFromPartialPostfixFallback` (`Parser.cs:367-484`) | **мёртвый** (Partial не порождается) | Удаляются (0.7) |
| `_partialMemo`, `_partialAccumulated` (`Parser.cs:48-49`) | мёртвый | Удаляются (0.7) |
| `RecoveryStackReconstructor` (`Recovery/RecoveryStackReconstructor.cs`) | мёртвый (пустой вход) | Удаляется (0.7), заменён `FailureSnapshot` |
| `ParseContext`/`ParseLocation` (`Recovery/ParseContext.cs`) | жив (но `Expected` почти всегда `[]`) | `ParseContext` остаётся в `Result.Partial`; `ParseLocation` → `FrameLocation` в `StackFrame`; `RecoveryOptions` переезжает в `Rules.cs` (§3.9) |
| Чистка memo (`Parser.cs:225-239`) | жив | Заменяется `Hygiene` (1.3) |
| Игнор memo в recovery-режиме (`Parser.cs:252-263`) | жив | Удаляется: memo доверен, кроме удалённых `Failure` на e |
| Тай-брейк `Parser.cs:336-343` | жив | Удаляется (0.3), заменён S0-приоритетом и Success-bites-Partial |
| Протокол `ErrorPos`/`_expected` (`Parser.cs:804-812`) | жив | Факторизуется в `ReportMismatch` (0.4), семантика сохраняется |
| Предикаты `AndPredicate`/`NotPredicate` (`Rules.cs:333-372`, `Parser.cs:646-672`) | жив | Основа спекулятивной валидации: engine использует тот же изолированный механизм (§3.3.4) для проверки якорей S2 |
| `GetExpectedTerminals` (`Parser.cs:899-912`) | жив (неточный) | Заменяется `FirstSets.Get` (0.3) |

---

## 7. Статус работы (точка продолжения для новой сессии)

Документ пишется частями; этот раздел обновляется в последнюю очередь. Если контекст сжат/потерян — начинать отсюда.

### Готово в документе
§0 (таблица отличий от v1, 12 пунктов) · §1 (контекст, почему итеративный подход, таблица заимствований из Roslyn — вкл. hand-written предикаты → Anchors/CanStart) · §2 (понятия: точка восстановления E, resync-якорь, completion stack) · §3.1 (главный цикл: акцепт iff E2 > E, откат, лимиты) · §3.2 (FailureSnapshot + StackFrame: живой снимок на самом дальнем mismatch, push/pop в finally) · §3.3 (терминальный кэш + слой инъекций, ReportMismatch, единый TerminalComparer, Speculative-изоляция) · §3.4 (кандидаты: **S0 < S1 < S2 (resync T1/T2) < S3 (токен-терминатор) < S4 (EOF) < S5 (хвост)**, детерминизм) · §3.5 (патч-лог, rollback, гигиена = только Failure на e, обратный индекс) · §3.6 (Partial как «сбой с прогрессом»: базовый случай в ParseSeq, тай-брейк Success-bites-Partial, финальные состояния) · §3.7 (инварианты I1–I6) · §3.8 (дерево: IsRecovery на узле, RecoveryDiagnostic, семантика ErrorInfo) · §3.9 (RecoveryRule/RecoveryOptions: Terminators, Anchors, CanStart, TryInsert, MaxSkip, Recoverable + пример MiniC) · §3.10 (исправления FollowSetCalculator) · §4 (фазы 0–3) · §5 (риски) · §6 (судьба существующего кода).

### TODO (проверять grep'ом, не по памяти)
1. ✅ Выполнено: `SkipTo` полностью заменён на Anchors/CanStart (в документе 0 вхождений); битые ссылки исправлены: `§3.4.1` → «§3.4, S1», `§3.4.2` → «§3.4, S3», `§3.3.2` (про снимок) → §3.3.4, «§3.6» (триггер S0) → «§3.4, S0»; T1/T2-тесты в E2E-наборе; риск спекулятивной валидации и строка про предикаты на месте.
2. ✅ Выполнено (проверка консистентности по коду через Roslyn MCP):
   - Гигиена — часть `candidate.Apply` (атомарно с патчами, всё в MemoPatch-логе, откат включает Hygiene) — иначе ре-парсинг S1 упирается в `Failure`-memo на `e` и ни один кандидат не принимается;
   - скан S2 начинается с `S = e` (Примеры 1/2 при S == e воспроизводимы; S3 остаётся от `e+1` — нулевой skip ведёт S1);
   - финальная `ErrorInfo` в §3.1 соответствует таблице §3.6 (null только для Success@EOF и Partial@EOF);
   - неявный S0-кандидат в главном цикле — легаси-путь (RecoveryPrefix/Postfix/OftenMissed) сохраняется от Фазы 1.1 до появления engine'а;
   - `Speculative`-хелпер: save/restore единственного `_lastSnapshot` (без истории/счётчиков — согласовано с §3.2);
   - `RecoveryCandidate` имеет поля сортировки (Rank/Pos/RuleName/TerminalKind) — сортировка §3.4.3 реализуема;
   - правила v1-чистки перечислены полностью (включая две `Failure`-правилы);
   - список вхождений `_recoverySkipPos` полон (16 мест, проверено семантическим поиском);
   - §0(11): `Test_ParsingReachesEndOfString` уточнён как план Фазы 5.2 v1 (не реализован), а не существующий тест.
3. Перегенерировать `RecoverySystemChecklist.md` по Фазам §4 (старый — по v1).
4. В шапке `RecoverySystemPlan.md` (v1) пометить superseded.
5. Реализация — с Фазы 0 (исправления Parser.cs); регрессия: деревья существующих грамматик (JSON, Cpp, DOT, CsNitra) на корректных входах не должны меняться.

### Ключевые решения (не потерять при сжатии контекста)
- **Ядро (идея пользователя, сохранено):** итеративный ре-парсинг + модификация memo. Падение правила — норм (longest-match, параллельные альтернативы); ошибка известна только на верхнем уровне (не EOF / Partial). memo + MaxFailPos + снимок = полная картина; префикс `[0..E)` воспроизводится из memo почти бесплатно.
- **E (точка восстановления):** Failure → `ErrorPos`; Partial → `max(NewPos, ErrorPos)`; Success < EOF → `NewPos` (хвостовой мусор).
- **EOF — не токен** (PEG-парсер, терминала конца входа в грамматике нет): «добрали до конца» = `NewPos == input.Length` (`Parser.cs:181`); для follow-sets EOF эмулируется `EofTerminal` (`TryMatch` = 0 iff `pos >= input.Length`, `FollowSetCalculator.cs:14-18`). Вынести в **общий стабильный синглтон** (сейчас приватный non-singleton); единый `TerminalComparer` сравнивает по ссылке к синглтону. В recovery: «совпадение EOF» = `pos == input.Length` (**не** regex); EOF **никогда не вставляется** инъекцией; S4 = resync `S := input.Length` (валиден по определению).
- **Акцепт кандидата только при E2 > E**, иначе откат (I1). Префикс не чистится никогда (I2). Лимиты: 3 попытки/позиция, 64 итераций (I3). Полная сортировка кандидатов (I5). Ничего не теряется (I4). Корректный код не меняется (I6).
- **FailureSnapshot** — живой снимок стека (StackFrame: правило, прецедент, локация Seq/Loop/Postfix/Alt, Expected, Options) при самом дальнем mismatch; НЕ реконструкция из memo (v1-реконструктор отменён — он работал по пустому множеству: Partial не порождался).
- **Partial** — базовый случай: сбой в середине Seq с прогрессом (`ParseSeq`, `elemIdx > 0`); текущий код Partial нигде не порождает (все 13 мест — пропагация) → Фаза 0.3. Тай-брейк: Success бьёт Partial при равной длине.
- **Инъекции** — отдельный слой поверх чистого `_terminalCache` (TryMatch чист, mismatch `-1` не чистится никогда). Побочные эффекты (ErrorPos/_expected/снимок) — всегда через `ReportMismatch`, и на cache hit. Спекулятивные парсинги изолированы (`Speculative`-хелпер: предикаты, speculative-проверка SeparatedList, pre-parse engine'а).
- **Tier'ы resync:** T1 — полный спекулятивный parse якоря (авторские `Anchors` ?? выводимые из элементов циклов кадров: `ZeroOrMany(Ref("Statement"))` → `Statement`); T2 — авторские мягкие `CanStart` (аналог Roslyn `IsPossibleStatement`/`CanStartMember`; никогда не выводятся; нужны, когда следующий код тоже бит); T3 — токен-терминаторы follow-множеств (S3); T4 — EOF (S4). «Ложная» T2-точка безопасна: кандидат принимается только при E2 > E (I1), следующая итерация продолжит.
- **Completion stack:** внутренний кадр — абсорбер `[e..S)` (S > e) или нулевая вставка (S == e); внешние Seq-кадры — суффиксные обязательства (First не-nullable элементов i+1..end) вставляются в S, от внутреннего наружу.
- **Гигиена:** удалять только Failure на e для правил из снимка (все прецеденты), через обратный индекс `pos → keys`. Чистка v1 была ошибочна: удаляла собственные патчи.
- **Дерево:** `IsRecovery` остаётся на узле (дерево самодостаточное); v1-идея внешнего `_skippedTextMap` отклонена. Диагностика — `Parser.RecoveryDiagnostics`; `ErrorInfo` — только при невосстановленном результате.
- **Cost:** вставка = 1; skip = число небелых слов + число переносов строк в регионе; tier-penalty T1:0, T2:1.
- **Сохраняются (живой код):** RecoveryPrefix/RecoveryPostfix (триггер → переименование `_recoverySkipPos` → `_recoveryPoint`), OftenMissed (сахар над TryInsert), RecoveryTerminal/EmptyTerminal. **Удаляется (мёртвый код):** ContinueFromPartial, TryRecoverFromPartial, MergeWithPartialTree, `_partialAccumulated`, `_partialMemo`, RecoveryStackReconstructor, хак `Parser.cs:336-343`.
- **Карта Roslyn:** missing token → пустой `TerminalNode(IsRecovery)`; `SkippedTokensTrivia` → абсорбер-узел; `TerminatorState` (~100 save/restore, `LanguageParser.cs:57-137`) → данные `RecoveryOptions`; `SkipBadTokens*` → S3 (+счётчик вложенности скобок, `LanguageParser.cs:2078-2152`); `ConsumeUnexpectedTokens` (`LanguageParser.cs:14673`) → S5; `IsMakingProgress` → I1.
