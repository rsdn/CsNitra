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
