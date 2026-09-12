# Recovery — Handoff (сессия 1, 2026-09-12)

## Прогресс
| Пункт | Статус | Коммит |
|---|---|---|
| 0.1 StackFrame дисциплина | [x] | `8ffa16c` |
| 0.2 FailureSnapshot + Speculative | [x] | `58517fc` |
| 0.3A FirstSets + ParseContext→FrameLocation | [x] | `a22c6d4` |
| 0.3B Partial base case + tie-break + guard нуля | [x] | `2427ad9` |
| 0.4 Terminal cache + инъекции + TerminalComparer | [x] | `3599eb5` |
| 0.5 FollowSetCalculator (вложенные циклы) + GetTerminators | [x] | `e0c3669` |
| 0.6 RecoveryDiagnostic | [x] | `661d347` |
| 0.7 Dead code v1 + `_recoveryPoint` | [x] | `0e63fb3` |
| 1.1 Итеративный цикл + S0 | [x] | `f013327` |
| 1.2 RecoveryEngine.Generate S1–S5 | [x] | `0a5e082` |
| 1.3 MemoPatch + Hygiene + интеграция в цикл | [x] | см. git log (после `0a5e082`) |
| 1.3.1 OOM/время тестов (ParseSeparatedList) | [x] | там же |
| **1.4** Семантика финала §3.6 | **следующий** | |

Тесты: `Tests/ParserTests` — **242 passed / 0 failed / 2 skipped** (прогон < 1с). Сборка `Nitra.sln` — 0 ошибок.

## Отклонения от плана (все задокументированы в чек-листе)
1. **0.3B — hack НЕ удалён** (перенос в 1.5): `else if (postNewPos == maxPos && bestResult == null && isRecoveryPos)` в `ParseSeq` (Parser.cs ~345). Удаление ломает 7 MiniC Error-rule тестов. **1.5 обязан убрать его и вернуть тесты в зелёные.**
2. **1.1 — `RecoveryPointOf`**: для `Success < EOF` берётся `max(NewPos, ErrorPos)`, а не `NewPos` (дегенеративный Success@0).
3. **1.2 — S5 упрощён**: абсорбер = инъекция `Absorb` на первый терминал верхнего кадра; `FindSeq` — первый Seq с `> elementIndex` элементов; якоря/CanStart только `Ref`.
4. **1.3 — Hygiene РАСШИРЕННЫЙ scope (отклонение от I2)**: (a) Failure на e для правил снимка; (b) start-правило на currentStartPos ЛЮБОГО типа; (c) ВСЕ stale Failure (любая позиция). Удаляются Failure в префиксе `[currentStartPos, e)` — I2 нарушается, избежать не удалось (узкий scope не проходит recovery-тесты: Error-правила переиспытывают правила, упавшие в разных позициях). Success/Partial префикса не трогаются. **Кандидат на улучшение в 2.x:** сузить Hygiene до §3.5, если позволит.
5. **1.3.1 — откат `ErrorPos`+`_expected`** вокруг re-parse кандидата в recovery-цикле (нет прогресса → откат) — не было в плане, без него speculative-кандидаты загрязняли `_expected`.

## Процессы (обязательно для следующей сессии)
- **Один субагент за раз** (general). В промпте: путь к плану, ОДИН пункт, краткость («не рассуждай — проверяй на практике»), MCP для кода, сборка/тесты ТОЛЬКО из консоли (bash), логи в `C:\Users\user\AppData\Local\Temp\opencode\*.log`, UTF-8 без BOM / CRLF / точечные edit, НЕ коммитить. После — верификация (субагент или main), `[x]` в чек-листе, коммит.
- **Кодировка (критично):** bash-тул = Windows PowerShell 5.1. Кириллица ломает пайплайн (субагент читал фейлы как пассы). Первая инструкция каждой команды: `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $env:DOTNET_CLI_UI_LANGUAGE="en";` (пользователь поставил `setx /M DOTNET_CLI_UI_LANGUAGE en` — проверять `echo $env:DOTNET_CLI_UI_LANGUAGE`, в текущей сессии может не быть).
- **Субагенты сгорают по контексту** на больших пунктах → пункты дробить (0.3 → A/B). Пустой результат task = субагент умер — проверять `git status`/сборку/тесты, что успел, и запускать нового с остатком.
- **RoslynMCPServer** лочит dll в `bin/`: сборка упала на locked dll → повторить; повторный провал → `Stop-Process -Name RoslynMCPServer -ErrorAction SilentlyContinue` + перезапуск MCP (OpenCode).
- **netstandard2.0** в `ExtensibleParser`: нет `String.Contains(string,StringComparison)`, `List<T>[^1]`, `HashCode.Combine`; ImplicitUsings=disable. Конвенции — `AGENTS.md`.
- Чек-лист: `docs/RecoverySystemChecklist.md` (статусы + заметки по каждому пункту). План: `docs/RecoverySystemPlan.v2.md`.

## Инциденты сессии
- Субагент 1.3 умер (зациклился, сжёг контекст) — работа была почти готова; продолжил новый субагент.
- Субагент «закрытия пробелов» умер с пустым результатом — оставил PatchRollbackTests (3/6 падали) и расширенный Hygiene; догнал третий субагент.
- **Баг, найденный PatchRollbackTests:** `RecordInjection` записывал `default(Injection)` (record struct) как `OldValue` при отсутствии ключа → Rollback восстанавливал нулевой struct вместо удаления → фантомная инъекция. Фикс: `null` при отсутствии ключа.
- OOM в `RequiredCallWith{1,2,3}Args` (MiniC): `Injection.Insert(",")` на разделителе + ε-элемент → нулевой прогресс цикла `ParseSeparatedList` → бесконечный цикл. Фикс: guard нуля (Log + Failure) + откат `ErrorPos`/`_expected`.

## Следующие шаги
1. **1.4** — семантика финала §3.6: `ErrorInfo`/`RecoveryDiagnostics`, Partial-при-EOF как восстановленное.
2. **1.5** — удалить hack 0.3B (Parser.cs ~345), вернуть 7 MiniC Error-rule тестов.
3. 2.x — оптимизации (кандидат: сузить Hygiene до §3.5), 3.x — полировка/документация.
4. Все фазы тестировать на MiniC (`Tests/ParserTests/MiniC/`).
