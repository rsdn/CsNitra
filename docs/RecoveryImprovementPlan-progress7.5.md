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
