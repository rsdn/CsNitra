# Исследование Nitra: парсер и система восстановления

Отчет об исследовании кодовой базы Nitra (`C:\RSDN\nitra`) — движка для создания расширяемых
языков программирования на Nemerle с агрессивным использованием макросов (генерация кода
во время компиляции грамматики). Цель: понять архитектуру парсера, представление дерева,
кодирование частичного разбора и механизм восстановления после ошибок — как материал
для сравнения с `ExtensibleParser` (CsNitra) и улучшения последней.

---

## 1. Где код парсера

Парсер **генерируется на этапе компиляции грамматики** (эмиттеры + Nemerle-макросы);
в рантайме остаётся только «скелет».

### Эмиттеры (генерация кода) — `Nitra\Nitra.Compiler\Generation\Parser\`

| Файл | Роль |
|---|---|
| `RegularRuleParserEmitter.n` | генерирует FSM-сканер токена (чистый `goto`-код по символам, `FsmEmitter.n`) |
| `ParseMethodEmitter\CompileSequence.n` | **сердце**: генерирует `Parse()` для правил (последовательностей субправил), включая мемоизацию и partial-state |
| `ExtensionRuleParserEmitter.n` | prefix/postfix правила расширений (TDOPP-аналог) |
| `SimpleRuleParserEmitter.n`, `RuleParserEmitter.n` | базовые классы генерации |

### Рантайм — `Nitra\Nitra.Runtime\`

| Файл | Роль |
|---|---|
| `ExtensibleRuleParser\Parse.n`, `ParsePrefix.n`, `ParsePostfix.n`, `FindExtension.n`, `Constants.n` | драйвер расширяемого парсера и layout узлов |
| `Internal\RuleParser.n`, `SimpleRuleParser.n`, `StartRuleParser.n`, `TokenParser.n` | иерархия парсеров правил |
| `Internal\ExtensionRuleParserState.n` | выбор лучшей альтернативы (longest-match) |
| `ParserHost.n` | реестры `RuleParsers[ruleId]` и `ParsingStates[stateId]` |
| `Parsing\ParseResult.n` | контейнер дерева (`rawTree`, `memoize`), аллокатор, точки входа |
| `Parsing\IncrementalParser.n`, `ParseSession.n` | инкрементальный парсинг, конфигурация, хук `OnRecovery` |
| `ParseTree\*`, `RecoveryModeParseTreeReader.n` | высокоуровневое дерево поверх raw-дерева |
| `Internal\Recovery\RecoveryParser\*` | **система восстановления** (см. §5) |

---

## 2. Дерево разбора: два массива int

`ParseResult.n:33-34`:

```
rawTree : array[int]   // bump-аллокатор узлов: Allocate(size), ParseResult.n:299
memoize : array[int]   // индекс = позиция в тексте, значение = указатель на голову списка узлов
```

### Layout узла (`Constants.n:11-49`)

Узел — блок int'ов, начинающийся с указателя `ptr`:

| смещение | что хранит |
|---|---|
| `+0 (Id)` | `ruleId` + **старшие 2 бита — флаги** (`RawTreeFlags`: `Bad`/`Equal`/`Best`; маска `RawTreeMask.Id = ~(3 << 30)`) |
| `+1 (Next)` | следующий узел в **односвязном списке альтернатив** для этой позиции (вход в список — `memoize[pos]`) |
| `+2 (State)` | состояние узла: `RawTreeParsedState = ~int.MaxValue` = разобран целиком; `>= 0` = **частично** |
| `+3.. (Sizes)` | по int на каждую субправилу — её **размер** (длина в символах) |

Специальные узлы-обёртки: `PrefixId`/`PostfixId` с собственным layout
(`PrefixOfs`: Id, Next, List, MaxFailPos, NodeSize; `PostfixOfs`: + FirstRuleIndex).

### Как эмулируется структура

Родитель **не хранит указателей на детей**. Дети находятся «в тексте»:
позиция начала ребёнка = `begin + размер предыдущей субправилы`, а сам ребёнок ищется
через `TryGetRawTree(childPos, childRuleId)` — обход списка `memoize[childPos]`
(ParseResult.n:349). Размер правила = сумма Sizes-слотов (`GetRawTreeSize`, ParseResult.n:375).

Выбор лучшей альтернативы — longest-match: альтернативы сравниваются по токенам
(`TokenEnumerator1/2`), победитель помечается флагом `Best`, равные по длине — `Equal`
(неоднозначность) — `ExtensionRuleParserState.Append`, ExtensionRuleParserState.n:28.

### Свойства представления

- Компактность: аллокация = сдвиг указателя в массив; никаких объектов на узел.
- Скорость обхода: последовательный доступ по int'ам, кэш-дружелюбно.
- Цена: структура неявная; чтение требует walker'ов (`WalkerBase.n`); отладка — через
  `DebugText` (ParseResult.n:258) и визуализаторы.
- Инкрементальный парсинг (`IncrementalParser.n` + конструктор
  `ParseResult(old, head, tail)`): неизменённые голова/хвост переиспользуют старый
  `rawTree`, мемоизация копируется хвостом (ParseResult.n:67).

---

## 3. Как кодируется частичный разбор

Механизм целиком в генерируемом коде (`CompileSequence.n`) и драйвере:

1. **`State`-поле узла.** При входе в правило генерируемый код читает `memoize[curTextPos]`:
   - узел есть и `State == RawTreeParsedState` → **hit мемоизации**:
     `curTextPos += RawSize(...)` и пропуск (CompileSequence.n:80-84);
   - узел есть и `State >= 0` → правило **недопарсено**: с этой позиции разбор уже
     проваливался, сразу `curTextPos = -1` (fail-code).
2. **Последний Sizes-слот = MaxFailPos.** По fail-пути (CompileSequence.n:166-173) в
   последний слот записывается `parseResult.MaxFailPos` — позиция, где именно сломался
   разбор этого правила. Читается в ParsePrefix.n:58 и в `memoizeSimpleFailCode`.
3. **Extension-правила** переживают узел между вызовами через `resultRef` (ref-параметр
   `Parse`): при повторном входе `rawTreePtr = resultRef`; если `State` ещё не
   «parsed» — продолжение с текущего `curTextPos` (CompileSequence.n:109-118).
4. **Postfix-набор** дополнительно хранит `FirstRuleIndex` — индекс первого
   неспарсенного правила (ParsePostfix.n:52,88), что позволяет возобновить перебор
   postfix-правил с того же места.

Именно эти маркеры («обрывы») использует и recovery, и инкрементальный парсинг:
основной парсер пишет честное дерево и **честно фиксирует**, где и как он сломался.

---

## 4. Общий цикл парсинга

```
ParseSession.Parse()
  → ParseResult.Parse()                       (ParseResult.n:111)
    → RuleParser.Parse(StartPos, text, this)  (ExtensibleRuleParser.Parse)
        = ParsePrefix + цикл ParsePostfix      (longest-match по префиксам/postfix'ам)
    → при неудаче (res < 0 || не дошли до EOF || CompletionPos >= 0):
        MainParserFail = true
        ParseSession.OnRecovery(this)          (ParseResult.n:146)
        ErrorCollectorWalker → IsSuccess
```

Три стратегии восстановления (`ParseSession.n:95-97`):

- `SmartRecovery` → `RecoveryParser.RecoveryFromAllErrors()` (по умолчанию);
- `PanicRecovery` → классический panic-mode с вычислением follow-множеств
  «стоппер-токенов» (RecoveryParser.PanicRecovery.n);
- `FirstErrorRecovery` → минимальная: на первом fail вставляет пустые субправилы
  и отбрасывает хвост.

---

## 5. Система восстановления (SmartRecovery)

### 5.1. Что это

**Не рекурсивный спуск, а итеративный worklist-поиск по графу состояний** —
по сути GLR-чарт с ценами (edit-distance) и dataflow-фикс-пойнтом.

Ключевые структуры (`RecoveryParser.n`):

- `Records[pos] : Hashtable[ParseRecord → TokenChanges]` — какие
  `(ParsedSequence, state)` «живы» в позиции `pos` и с какой стоимостью;
- `ParseRecord(Sequence, State, ParsePos)` — структурный record, `State == -1` = завершён;
- `RecordsToProcess` — куча `(ParseRecord, TokenChanges)`, упорядочена по
  завершённости/стоимости/позиции;
- `Sequences[(pos, ParsingSequence)] : ParsedSequence` — накопленные
  `ParsedSubrules` и `Ends` (цены завершения в каждой позиции);
- `TokenChanges {Inserted, Deleted}` — стоимость: сколько токенов вставить/удалить;
  сравнение: минимум `Inserted+Deleted`, при равенстве — меньше вставленных
  (TokenChanges.n:60-71);
- `BestSolution` — лучшее завершение корня до конца текста.

### 5.2. Основной цикл (`RecoveryFromAllErrors`, RecoveryParser.n:59)

```
StartParseSequence(0, startRule); Parse();
while (BestSolution.IsFail) {
    ParseToFailPos();                        // догоняющий прогон до MaxPos
    curMaxPos = MaxPos;
    InsertSubrules(curMaxPos);               // ВСТАВКА: пустые субправилы (ε)
    DeleteTokenOrGarbage(curMaxPos, ...);    // УДАЛЕНИЕ: следующий токен/мусор как «ошибка»
    Parse();                                 // полный слив worklist
    when (timer.Elapsed > timeout)           // timeout = RecoveryTimeout (200 мс)
        { Delete(curMaxPos, Text.Length); Parse(); }
}
SaveRecoveredRawTreePart();
```

- `InsertSubrules` (RecoveryParser.Insert.n): для каждого незавершённого record'а в
  точке сбоя — `SubruleParsed(pos, pos, ...)` со стоимостью
  `MandatoryTokenCount` вставленных токенов, далее все next-состояния в ту же позицию.
- `DeleteTokenOrGarbage` (RecoveryParser.Delete.n): пробует «съесть» как мусор
  следующий токен (или интервал до него; `,`/`;` не удаляются; если токенов нет —
  до следующего токена); стоимость `deleted = 1`; на каждый кандидат — **полный**
  `Parse()`.
- `Parse()` (RecoveryParser.Parse.n:28): берёт record из кучи, `PredictionOrScanning` —
  по типу состояния (`Simple/Scan/Extensible/List/...`) либо реально парсит токен
  через обычный генерированный парсер (оптимизация: если без изменений — сразу
  `SubruleParsed`), либо раскручивает подпоследовательность. Завершённые record'ы
  в конце текста дают `BestSolution`.
- `ParseToFailPos` (RecoveryParser.n:157): вложенные фикс-пойнты
  (`do { ... Parse(); } while (count < Records[maxPos].Count)` внутри
  `while (maxPos < MaxPos)`) — догоняет все состояния, способные «съедать мусор».

### 5.3. Результат

`SaveRecoveredRawTreePart()` (RecoveryParser.SaveRecoveredRawTreePart.n) спускается от
корня: для каждой `ParsedSequence` считает согласованную цепочку субправил с
накопленными `TokenChanges` (обратный проход `GetSubrulesAndChanges`, инвариант
`startChanges + subruleChanges == endChanges`) и кладёт в
`ParseResult.RecoveredSequences[(start, end, sequence)]` →
`RecoveredSequence(Unambiguous|Ambiguous)` + пул `RecoveredSubrules`.

**rawTree не правится** — recovery строит параллельную структуру поверх.
Чтение: `RecoveryModeParseTreeReader.Read` (ParseTree\RecoveryModeParseTreeReader.n:14) —
если для `(start,end,seq)` есть восстановленная последовательность, дерево строится по
ней (с «missing»-узлами), иначе — стандартный проход по int-дереву.

### 5.4. Сильные стороны

- **Глобальная оптимальность** в пределах бюджета: минимизация числа изменённых
  токенов по всему входу, а не жадно по каждой ошибке.
- Не требует от автора грамматики никаких действий; единый механизм для recovery
  и completion (`CompletionPos` в той же `Records`-машине,
  `FilterKeywordsForCompletion`).
- Неоднозначность — first-class (`Ambiguous`).

### 5.5. Почему это тормозило (диагностика)

Алгоритм — **глобальный поиск edit-distance по GLR-графу с dataflow-фикс-пойнтом
по ценам**, у которого **нет ограничения работы на область** — только глобальный
wall-clock 200 мс (`RecoveryTimeout`, ParseSession.n:23).

Конкретные источники взрыва:

1. **Внешний цикл без бюджета на позицию.** Каждая итерация =
   `ParseToFailPos` (вперёд до `MaxPos` с внутренним фикс-пойнтом) + полный слив
   кучи `Parse()`. Файл с N ошибками → N полных проходов по чарту:
   O(шагов × размер чарта), а не O(шагов × локальная работа).
2. **Каскады улучшения цен.** `ParsedSequence.AddParsedSubrule` → при улучшении
   `UpdateSubrules` → `SubruleParsed` → запись обратно в кучу; `AddEnd` →
   уведомляет всех `Callers`. Улучшение цены в глубокой точке **не-локально**
   перезапускает работу в вызывающих. На входах с циклами + nullable-элементами
   (List с разделителем, ZeroOrMany) в точке ошибки возникает эпсилон-замыкание по
   циклическому графу состояний — много волн улучшений; плюс `RebuildHeap`
   (`IsRecordsToProcessCorrupted`) при каждом улучшении.
3. **Отравленная мемоизация.** Recovery повторно вызывает *те же сгенерированные
   парсеры*, которые читают `memoize` с частичными/failed-маркерами неудачного
   основного прохода. Специальные обходы (`FindExtension` пропускает Bad/partial,
   ParsePrefix.n:50-62 возвращает -1 на partial) вынуждают повторные исследования;
   взаимодействие тонкое — и медленность, и трудноотладимость.
4. **`DeleteTokenOrGarbage`** — на каждый кандидат-токен в точке сбоя полный слив
   worklist'а.
5. **Почему «иногда, и сложно выявить»:** ограничение *время*, а не *работа*.
   У отдельных входов (много ошибок, глубокие вложенные циклы, амбивалентность)
   поиск **наедает полный 200-мс бюджет** — «зависание ровно на 200 мс». Порог
   wall-clock → поведение зависит от машины/нагрузки: сценарий трудно воспроизвести
   и локализовать. **200-мс guard — пластырь над неограниченным поиском**, а не над
   конкретной медленной операцией.

### 5.6. Вывод для CsNitra

Правильная философия — противоположная: **не глобально оптимизировать, а
детерминированно деградировать** по лестнице стратегий с ограничением работы
(work-bound) и гарантированным грубым дном (кандидат «всегда даёт прогресс»).
Тогда и латентность ограничена конструктивно, и файл всегда допарсится.
