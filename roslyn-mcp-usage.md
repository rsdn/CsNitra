# Roslyn MCP — usability report (3.0c session)

Контекст: реализация 3.0c (размещение абсорбера S2/S3 на уровне цикла) в `ExtensibleParser/Recovery/RecoveryEngine.cs` + обновление/добавление тестов. Windows PowerShell 5.1, `Nitra.sln`, SDK 8.0.100 (rollforward).

## Что использовалось и как

| Инструмент | Зачем | Как пошло |
|---|---|---|
| `roslyn_run_dotnet_build` | компиляция после правок | **Отлично.** Быстро, корректно ловит ошибки, `--no-incremental` по умолчанию. Использовал как основной gate после каждого edit. |
| `roslyn_run_specific_test` | проверка отдельных классов тестов | **Отлично.** Быстро (<1с), чистый `Total/Passed/Failed`. Главный инструмент TDD-цикла. `className` резолвится по FQN через Roslyn (без ручных `--filter`). |
| `roslyn_run_dotnet_test` | полный прогон `ParserTests` | **Проблема.** На ~300 параллельных тестах упирается в **MCP request-таймаут** (`-32001 Request timed out`), даже с `timeoutSeconds=180`. Это overhead VSTest в этой среде, **не** hang. Обход: `dotnet test --no-build` через bash с большим таймаутом → `Passed: 287, Failed: 0, Skipped: 3`. |
| `roslyn_search_code` | поиск по исходникам | **Отлично.** Быстро, ripgrep. |
| `roslyn_get_method_body` | чтение конкретного метода | **Отлично.** Не засоряет контекст целым файлом. |
| `roslyn_get_diagnostics_for_file` | диагностика конкретного файла | **Отлично.** |
| `read` / `edit` / `write` / `grep` / `glob` | файлы | Стандартно, без замечаний. |

## Замечания / грабли

1. **Полный прогон тестов через MCP — не работает по таймауту.** Для решения `Nitra.sln` / проекта `ParserTests` с ~300 тестами `roslyn_run_dotnet_test` не дотягивает до завершения (request-таймаут клиента). Для «всё ли зелёное» использовать `dotnet test --no-build` через bash. Для целевой проверки — `roslyn_run_specific_test` (он быстрый и надёжный).
2. **SDK-предупреждение при каждой сборке:** `Pinned SDK 8.0.100 was not found` (global.json) — фактический SDK 8.0.411 через rollforward. Не блокирует, но шумит в каждом build-ответе.
3. **`MM` в git status** (StackGuardTests.cs): в индексе осталась staged-правка прошлой сессии (удаление `using ExtensibleParser.Recovery;`), поверх — моя unstaged-правка ассерта. Суммарно корректно, но при будущем коммите следить, чтобы не ушёл только staged-слой.

## Вывод

MCP-инструменты для **поисков, чтения, compile-gate и целевых тестов** — основной рабочий инструмент, быстрее и чище bash. Единственное системное ограничение — **полный прогон большого test-проекта** (request-таймаут); его закрывает `dotnet test` через bash. Рекомендация: держать правило «`run_specific_test` для TDD, bash `dotnet test` для полного прогона».

## 3.1.1 session

Контекст: item 3.1.1 — переписать `Test_TrailingGarbage` (E2E-тест 5) на true-EOF trailing-garbage, снять `[Ignore]`, поднять бюджет до 16. Тест-только, движок не трогал.

- **`roslyn_run_dotnet_build`** (`Nitra.sln`) — отлично, 0 ошибок, `--no-incremental`. Основной compile-gate.
- **`roslyn_run_specific_test`** (`MiniCEndToEndTests` / `Test_TrailingGarbage`) — отлично, `Total: 1 · Passed: 1 · Failed: 0`, <1с. FQN резолвится через Roslyn без ручного `--filter`.
- **bash `dotnet test Tests/ParserTests --no-build --nologo`** — единственный путь для полного прогона. `Passed: 288, Failed: 0, Skipped: 2` (Total 290). Ранее-игнорированный тест теперь выполняется и проходит → passed 287→288, skipped 3→2.
- **`read` / `edit` / `grep`** — стандартно, без замечаний.

Замечания / грабли:
1. **Полный прогон через MCP — не работает по таймауту** (`roslyn_run_dotnet_test` → `-32001 Request timed out`, даже с `timeoutSeconds=180`). Обход по правилу: bash `dotnet test --no-build` после MCP-build. Подтверждено и в этой сессии.
2. **SDK-предупреждение при каждой сборке:** `Pinned SDK 8.0.100 was not found` (global.json), фактический 8.0.411 через rollforward. Не блокирует, шумит в каждом build-ответе.
3. **Подтверждена корректность счёта:** снятие `[Ignore]` меняет passed/skipped (287/3 → 288/2), Total 290 стабилен. При проверке «всё ли зелёное» важно сверять именно Total и Failed, а не только passed.

Вывод: MCP (build + targeted test + поиск/чтение) — основной инструмент; полный прогон — только bash. Рекомендация MCP: дать `run_dotnet_test` опцию «запустить и вернуть PID/журнал» или стриминг-прогресса, чтобы не упирается в request-таймаут на ~300 параллельных тестах; либо поднять клиентский таймаут отдельно для full-suite.

## 3.1.2a session

Контекст: item 3.1.2a — добавить 4 инвариант-теста (6–9: I6/I4/reaches-EOF/I5) в `MiniCEndToEndTests.cs` + приватные хелперы. Тест-только, движок не трогал.

| Инструмент | Как пошло |
|---|---|
| `roslyn_run_specific_test` | **Отлично** (4/4 `<1с`, `Total:1 Passed:1 Failed:0`). FQN резолвится через Roslyn без ручного `--filter`. Главный TDD-инструмент. |
| `roslyn_find_symbol_definition` / `roslyn_get_diagnostics_for_file` / `read`/`edit`/`grep` | **Отлично.** Быстро, без замечаний. |
| `roslyn_get_test_list` | **Отлично** для диагностики: подтвердил, что workspace видит 4 новых теста (FQN есть) — т.е. проблема не в workspace, а в stale-DLL. |
| `roslyn_reload` | **Необходим:** после правок workspace был stale; reload подхватил новые методы. |
| `roslyn_run_dotnet_build` | **Проблема (см. ниже).** Отчитывался «Build succeeded», но НЕ обновил тестовую сборку. |
| bash `dotnet test Tests/ParserTests --no-build --nologo` | **Отлично** для полного прогона. `Passed: 292, Failed: 0, Skipped: 2` (Total 294 = 288 + 4 новых). |

Замечания / грабли:
1. **`roslyn_run_dotnet_build` не обновил `ParserTests.dll`.** После правок и двух MCP-build'ов (оба «Build succeeded») `bin\x64\Debug\net8.0\ParserTests.dll` остался со старым таймстампом (17:47 < правки 17:59). Следствие: `roslyn_run_specific_test` c `noBuild=true` → «no matching tests» (FQN резолвится в workspace, но метода нет в скомпилированном DLL). Обход: `roslyn_run_specific_test` c `noBuild=false` (сам делает `dotnet build`) — сразу нашёл и прогнал тест. **Рекомендация:** в диагностике «no matching tests» явно различать «FQN нет в workspace» и «FQN есть в workspace, но отсутствует в DLL» (второй случай → подсказать `noBuild=false`/rebuild); либо гарантировать, что `run_dotnet_build` реально пересобирает тестовые проекты (сейчас `--no-incremental` не помогает).
2. **Platform-рассогласование.** Workspace загружен с `Platform=x64` → MCP-сборки/`run_specific_test` используют `bin\x64\Debug\`, а голый bash `dotnet test` (без Platform) — `bin\Debug\`. Два разных DLL. Для `--no-build` bash-прогона важно, чтобы нужный DLL был свеж (в сессии оба оказались свежи, но это хрупко).
3. SDK-предупреждение `Pinned SDK 8.0.100 was not found` (фактический 8.0.411 через rollforward) — шумит в каждом build-ответе, не блокирует.

Вывод: MCP (targeted test + поиск/чтение/diagnostics + reload) — основной инструмент; полный прогон — bash. Новая грабо: **stale тестовый DLL после `run_dotnet_build`** → для надёжности TDD-цикл вести `run_specific_test` c `noBuild=false` (или bash-build перед `--no-build` прогоном).

## 3.2 session

Контекст: item 3.2 — **только документация** (`docs/RecoveryAuthorGuide.md`) + обновление чек-листа. Код не тронут, сборка/тесты не запускались (по ТЗ).

- **MCP-инструменты не использовались.** Задача doc-only: чтение плана/чек-листа/кода и запись markdown. Для чтения использованы host-инструменты `read` / `grep` / `glob` / `edit` / `write` — их оказалось достаточно (никакой компиляции/тестов/семантического анализа не требовалось).
- **Что сработало:** host `read` (план/чек-лист/код) + `grep` (точные diagnostic-сообщения в `RecoveryEngine.cs`, ранги `Rank:`, `Unrecovery`/`IsRecovery`) + `edit` (чек-лист, append usage). Быстро, без MCP-оверхеда.
- **Чего не хватало / наблюдения:**
  1. Для doc-задач, ссылающихся на точный API/константы/сообщения, host `grep`/`read` лучше MCP: нет загрузки workspace, нет риска stale-DLL/таймаутов. MCP-инструменты (`run_dotnet_build`/`run_specific_test`/semantic) здесь **ненужны** и только бы добавили overhead (workspace-load минуты, request-таймауты).
  2. Точные diagnostic-строки и ранги кандидатов пришлось вытаскивать `grep`'ом по `RecoveryEngine.cs` (`Rank:` / `new RecoveryDiagnostic`) — MCP-semantic (`find_symbol_definition`) не дали бы больше, т.к. это строковые литералы, а не символы.
  3. `Unrecovered` — в enum, но движком не порождается; задокументировано как «запасной» (grep по `ExtensibleParser` → 1 вхождение, только определение).
- **Вывод:** для doc-only итераций держать правило «host read/grep/edit, без MCP»; MCP подключать только когда нужен compile-gate / целевой тест / семантика.
