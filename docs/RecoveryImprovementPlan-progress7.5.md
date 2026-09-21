# RecoveryImprovementPlan — Wave 7.5 (A4-7) progress

## 7.5.1 — A4-7 part 1: exact expected-set helper (build, not applied)

Цель (A4-7, `docs/antlr4-analysis.md` §A4-7): «expecting {...}» в финальном сообщении
(`FatalError`/`Unrecovered`) — не один упавший терминал (`_expected`), а полный контекстный
набор. Эта подточка (7.5.1) строит помощник; применение к `FatalError`/`Unrecovered` — 7.5.2.

### 1. Извлечённая общая функция суффиксных обязательств

`GetSuffixObligationFirsts(FailureSnapshot, Parser, int fromFrameIndex)` —
`ExtensibleParser/Recovery/RecoveryEngine.cs:992`.

Вынесена из механики S2/S4: обход Seq-кадров от `fromFrameIndex` вниз, для каждого — First всех
элементов ПОСЛЕ текущего (`idx+1..`), пропуск nullable-элементов и EOF/ε. Возвращает
`List<(Terminal T, string RuleName)>` — кадровая ассоциация (RuleName) сохранена, т.к.
S4-диагностика несёт имя правила кадра (S2 игнорирует — `_`). Чистая функция (только чтение
`Rules`/`FollowCalculator`, без инъекций/мутаций); фильтры `Injectable`/совпадения в позиции
остаются в вызывающем (специфика S2/S4).

Точки вызова (поведение сохранено):
- **S2** — `RecoveryEngine.cs:297`: `fromFrameIndex = Stack.Length - 2` (без падающего кадра —
  он обрабатывается выше: абсорбер/memo-патч); фильтры `!t.Injectable` и
  `t.TryMatch(input, resyncPos) >= 0` сохранены в S2.
- **S4** — `RecoveryEngine.cs:769`: `fromFrameIndex = Stack.Length - 1` (включая верхний кадр —
  достроение до EOF удовлетворяет и хвост падающего правила); фильтр `t.TryMatch(input, s) >= 0`
  и диагностика с `ruleName` сохранены в S4. (Переменная `calculator` в S4 удалена — стала
  неиспользуемой; в S2 остаётся — используется в `FirstMatchesAt`/jumpLiterals.)

### 2. Помощник объединённого expected-набора

`BuildExpectedSet(FailureSnapshot, Parser)` — `ExtensibleParser/Recovery/RecoveryEngine.cs:1023`.

`BuildExpectedSet = Expected верхнего кадра ∪ GetTerminators(стек) ∪ First суффиксных обязательств`:
1. `snapshot.Stack[^1].Expected` (Expected верхнего кадра);
2. `parser.GetTerminators(stack)` (per-site, A5-6; EOF добавляется самими GetTerminators);
3. `GetSuffixObligationFirsts(snapshot, parser, Stack.Length - 1)` (First суффиксных обязательств,
   S4-семантика — весь стек, включая верхний кадр; вызов на `RecoveryEngine.cs:1042`).

Дедупликация по `TerminalComparer.Instance`. Чистая функция (без побочных эффектов) —
тестируется в изоляции. **Не применена** к `FatalError`/`Unrecovered` (это 7.5.2).

### 3. Тест

`Tests/ParserTests/Recovery/ExpectedSetTests.cs` — 4 теста (класс `ExpectedSetTests`):
- `Test_Setup_TopFrame_Is_MidSeq0` — снимок падения: топ-кадр `Mid Seq(0)`, Expected содержит `m`.
- `Test_BuildExpectedSet_Is_UnionOfThreeSources` — `BuildExpectedSet` == объединение трёх
  источников (вычисленных независимо: top.Expected / GetTerminators / GetSuffixObligationFirsts).
- `Test_BuildExpectedSet_EachSourceContributes` — каждый источник вносит свой терминал:
  `m` (только источник 1), `z` (только источник 3), `EOF` (только источник 2).
- `Test_BuildExpectedSet_ExactKinds` — точный набор `{m, c, z, EOF}` (order-independent).

Грамматики теста: `Start := "a" Body "z"; Body := "b" Mid "c"; Mid := Seq("m")`, вход `"ab"`
(падение в `Mid` в EOF). Источники: s1(top Expected)={m}, s2(терминаторы)={c,EOF},
s3(суффиксные First)={c,z} → объединение {m,c,z,EOF}.

### 4. Результаты (one-shot, от `C:\RSDN\CsNitra`, Debug)

| Suite | Result |
|---|---|
| `Tests/ParserTests` | 428 passed, 0 failed, 2 skipped |
| `Tests/CSharpGrammarTests` | 1524 passed, 0 failed, 3 skipped |
| `Tests/CsPreprocessorTests` | 128 passed, 0 failed |

Поведение S2/S4 сохранено: полный `ParserTests` (включая `CandidateGenerationTests` —
явные S4-тесты суффиксных обязательств, напр. `S4:Item:EOF`) зелёный.

### Примечание (отклонение от ограничения)

В рабочем дереве `Tests/ParserTests/ParserTests.csproj` присутствовала НЕзакоммиченная
строка `<Compile Include="Recovery\ExpectedSetTests.cs" />` (остаток предыдущей сессии).
Она жёстко ломает сборку: файл существует → `NETSDK1022` (дублирование с дефолтным глобингом
SDK), не существует → `CS2001`. Обойти это без правки `.csproj` невозможно. Удалена эта одна
строка, вернув `.csproj` в закоммиченное (HEAD) состояние — т.е. относительно коммита
`.csproj` **не изменён** (`git diff HEAD -- .../ParserTests.csproj` пуст). Тестовый файл
`ExpectedSetTests.cs` собирается дефолтным глобингом SDK (как все остальные тесты проекта).

## 7.5.2 — A4-7 part 2: apply the combined set to the final message — **STOP (конфликт с фикс-тестом)**

Реализация выполнена в рабочем дереве (не закоммичена), но полный `ParserTests` НЕ зелёный:
лопнул один существующий тест, фиксирующий старое содержимое `FatalError.Expecteds`. По условию
задачи («If a test asserts the old single-terminal "expecting" and now breaks, STOP and report
(don't weaken)») — **STOP**, решение (поправить тест / уточнить источник / сузить применение)
осталось за владельцем.

### 1. Применение (изменён `ExtensibleParser/Parser.Recovery.cs`)

- **`FatalError` в `FinalizeResult`** (`Parser.Recovery.cs:587-594`): `e = RecoveryPointOf(...)`,
  затем `snapshot = FailureSnapshotAt(e)` (существующий helper: `_lastSnapshot` при `Pos == e`,
  иначе null — снимок в E доступен в скоупе без трездинга). `expecteds = snapshot is null
  ? _expected.ToArray() : Recovery.RecoveryEngine.BuildExpectedSet(snapshot, this).ToArray()`.
  `FailureSnapshotAt(e)` — тот же гейт, что и в цикле восстановления (линия 652).
- **`Unrecovered` (A4-2, `AddUnrecoveredIfS6`)** (`Parser.Recovery.cs:720-731`): локальный
  `snapshot` (снят в точке e ДО re-парса кандидата — ровно «FailureSnapshot at E») уже в скоупе
  локальной функции. При наличии снимка сообщение расширяется:
  `error at {e} not recovered (absorbed to EOF), expecting: <kinds BuildExpectedSet>`
  (набор в `Message`, т.к. запись `RecoveryDiagnostic` несёт только один `Terminal?` и менять
  её нельзя — за пределами разрешённых файлов). `Kind`/позиции/`Terminal=null`/`RuleName`
  не изменились — A5-4 дно-контракт и порядок 6.1.2c не затронуты (Unrecovered по-прежнему
  нулевой маркер, добавляется последним).

### 2. No-snapshot fallback (старое поведение сохранено)

- `FatalError`: `snapshot == null` → `_expected.ToArray()` (как было).
- `Unrecovered`: `snapshot == null` → старое сообщение без «expecting»
  (`error at {e} not recovered (absorbed to EOF)`) — зафиксированная форма 1.3.4 не меняется.

### 3. Тест — `Tests/ParserTests/Recovery/ExpectedSetApplicationTests.cs` (новый, 4 теста, все зелёные)

Грамматика формы 7.5.1 (`Start := "a" Body "z"; Body := "b" Mid "c"; Mid := Seq("m")`, вход
`"abX"` — падение в e=2, снимок в e: топ `Mid Seq(0)` Expected={m}; источники {m}/{c,EOF}/{c,z}
→ {m,c,z,EOF}):
1. `Test_FatalError_WithSnapshotAtE_FullExpectedSet` — `MaxRecoveryIterations=0` → FatalError@2,
   `Expecteds` == {m,c,z,EOF} (топ Expected «m», терминатор «EOF», суффиксный First «z»).
2. `Test_FatalError_NoSnapshotAtE_OldExpectedBehavior` (контроль) — `Start := "a"`, вход `"b"`:
   mismatch в pos 0 == ErrorPos → снимка нет → `Expecteds` == {a} (старый один терминал).
3. `Test_Unrecovered_WithSnapshotAtE_FullExpectedSetInMessage` — `ForcedDegradationLevel=3`
   (форс-дно S6) → Success@EOF, ErrorInfo=null (A5-4), ровно 1 Unrecovered@2, сообщение несёт
   полный набор {m,c,z,EOF}.
4. `Test_Unrecovered_NoSnapshotAtE_OldMessage` (контроль) — `Module := Expr Expr`, вход
   `"12 34 ###"` (Success до EOF, mismatch «до» e) → Unrecovered со СТАРЫМ сообщением без
   «expecting».

### 4. Результаты (от `C:\RSDN\CsNitra`, Debug)

| Suite | Result |
|---|---|
| `Tests/ParserTests` | 431 passed, **1 failed**, 2 skipped (434) |
| `Tests/CSharpGrammarTests` | 1524 passed, 0 failed, 3 skipped |
| `Tests/CsPreprocessorTests` | 128 passed, 0 failed |

Единственный лопнувший тест: `Recovery.SnapshotTests.Test_Speculative_AndPredicate_DoesNotPollute_Expected`
(`Tests/ParserTests/Recovery/SnapshotTests.cs:122`).

### 5. Корень конфликта (почему «y» попал в набор)

Грамматика теста: `Start := Seq("a", &Seq("b","c"), "y") | Seq("a","z")`, вход `"a b Q"`.
Падение в e=2: топ-кадр `Start Seq(1)` Expected={z}, стек `[Start RuleFrame(AltIndex=1),
Start Seq(1)]`. `BuildExpectedSet` = {z} ∪ {EOF} ∪ **{y}**: суффиксный источник
(`GetSuffixObligationFirsts`) для кадра `Start Seq(1)` вызывает `FindSeq(parser, "Start", 1)`,
а `FindSeq` — «упрощение 1.2»: **первый** Seq в альтернативах правила с числом элементов >
elementIndex → возвращает Seq ПЕРВОЙ альтернативы (`a, &bc, y`), а не реально выполнявшейся
(альтернатива 1: `a, z` — у которой хвост после ei=1 пуст). AltIndex (`RuleFrameLocation`)
в кадре есть, но `FindSeq` его не читает. Итог: `Expecteds = {z, EOF, y}`, тест фиксирует
`!Contains("y")` (старая семантика «только терминалы, упавшие в самой дальней точке» = {z}).

Это неточность самого 7.5.1-помощника (общая с S2/S4 — `FindSeq` используется всеми тремя),
а не ошибки применения в 7.5.2. Варианты развязки (выбор за владельцем):
- (a) уточнить `FindSeq`/`GetSuffixObligationFirsts` с учётом `AltIndex` нижележащего
  RuleFrame-кадра (изменит поведение S2/S4 — потребует отдельного под-пункта + ре-прогона
  CandidateGeneration/S2/S3/S4-тестов);
- (b) поправить `SnapshotTests.Test_Speculative_AndPredicate_DoesNotPollute_Expected` под
  новую (расширенную) семантику `Expecteds` — только с явного одобрения (запрет на
  ослабление тестов);
- (c) сузить применение `BuildExpectedSet` — но любой гейт под конкретный тест будет
  импровизацией (запрещено).

### 6. Файлы (рабочее дерево, НЕ закоммичено)

- `ExtensibleParser/Parser.Recovery.cs` — изменён (2 места: `FinalizeResult`, `AddUnrecoveredIfS6`).
- `Tests/ParserTests/Recovery/ExpectedSetApplicationTests.cs` — новый тест (4 теста).
- `Tests/ParserTests/ParserTests.csproj` — в рабочем дереве была НЕзакоммиченная строка
  `<Compile Include="Recovery\ExpectedSetApplicationTests.cs" />` (тот же паттерн, что в 7.5.1:
  остаток предыдущей сессии; ломала сборку NETSDK1022 — дублирование с дефолтным глобингом SDK).
  Удалена → `.csproj` совпадает с HEAD (`git diff HEAD` пуст); тест собирается дефолтным
  глобингом SDK.
- `RecoveryEngine.cs`, стратегии S0–S6, depth-guard, `RecoveryDiagnostic.cs` — НЕ тронуты.

## 7.5.3 — A4-7 part 3: AltIndex-aware suffix obligations (разблокирует 7.5.2)

Развязка варианта (a) из 7.5.2: уточнить суффиксный источник с учётом `AltIndex` нижележащего
RuleFrame-кадра — **без** изменения поведения S2/S4. Конфликт 7.5.2 снят: лопнувший тест
`Test_Speculative_AndPredicate_DoesNotPollute_Expected` снова зелёный, ослаблять его не пришлось.

### 1. Фикс (изменён `ExtensibleParser/Recovery/RecoveryEngine.cs`, 3 места)

- **`FindSeq(parser, ruleName, elementIndex, int? altIndex = null)`** — `RecoveryEngine.cs:977`.
  Новый опциональный `altIndex`: `null` (по умолчанию) → старый «упрощение 1.2» (первый Seq по
  всем альтернативам); `!= null` → только `alternatives[altIndex]` (активная альтернатива).
- **`GetSuffixObligationFirsts(..., bool respectAltIndex = false)`** — `RecoveryEngine.cs:1000`.
  Новый опциональный `respectAltIndex`: `false` (по умолчанию) → `FindSeq` без `altIndex`
  (старый путь); `true` → `altIndex = ActiveAltIndex(snapshot, i, frame.RuleName)` на каждый
  Seq-кадр.
- **`ActiveAltIndex(snapshot, frameIndex, ruleName)`** — `RecoveryEngine.cs:1029` (новый private
  helper). Возвращает `AltIndex` кадра непосредственно ниже (`snapshot.Stack[frameIndex - 1]`):
  только если он `RuleFrameLocation` с тем же `RuleName` (паттерн `FollowSetCalculator.TailOf`);
  иначе (`frameIndex < 1` / другая структура) → `null` → `FindSeq` остаётся на упрощении 1.2.
- **`BuildExpectedSet`** — `RecoveryEngine.cs:1066`: источник 3 теперь
  `GetSuffixObligationFirsts(snapshot, parser, Stack.Length - 1, respectAltIndex: true)`.

### 2. Почему S2/S4 НЕ изменились

S2 (`RecoveryEngine.cs:297`) и S4 (`RecoveryEngine.cs:769`) вызывают `GetSuffixObligationFirsts`
с тремя аргументами → `respectAltIndex` = `false` (дефолт) → `FindSeq` вызывается с
`altIndex = null` → ровно старый «упрощение 1.2». Два других вызова `FindSeq`
(`RecoveryEngine.cs:244`, `:664`) тоже трёхаргументные → `altIndex = null`. Единственный, кто
включает `respectAltIndex`, — `BuildExpectedSet` (7.5.2-применение к `FatalError`/`Unrecovered`).
Поведение S2/S4 побайтово идентично.

### 3. Результат (one-shot, от `C:\RSDN\CsNitra`, Debug)

| Suite | Result |
|---|---|
| `Tests/ParserTests` | 432 passed, 0 failed, 2 skipped (434) |
| `Tests/CSharpGrammarTests` | 1524 passed, 0 failed, 3 skipped |
| `Tests/CsPreprocessorTests` | 128 passed, 0 failed |

Ранее лопавшийся `Recovery.SnapshotTests.Test_Speculative_AndPredicate_DoesNotPollute_Expected`
(`SnapshotTests.cs:122`, `!Contains("y")`) — **зелёный**: топ-кадр `Start Seq(1)` в альтернативе 1
(`a, z`) теперь берёт хвост именно из альтернативы 1 (пуст), а не из альтернативы 0 (`a, &bc, y`),
так что «y» в `Expecteds` больше не утекает. `Expecteds = {z, EOF}`.

### 4. Файлы (рабочее дерево, НЕ закоммичено)

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — изменён (3 места: `FindSeq`,
  `GetSuffixObligationFirsts` + новый `ActiveAltIndex`, `BuildExpectedSet`).
- `docs/RecoveryImprovementPlan-progress7.5.md` — этот раздел.
- Прочее (`Parser.Recovery.cs`, `ExpectedSetApplicationTests.cs`, тесты, `.csproj`) — не тронуто.
  `.csproj` не изменялся. Коммит не делался.
