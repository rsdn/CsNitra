# Волна 0 — D1 корпус + D2-ядро отчёт (прогресс 0.1)

Статус: Готово.

## Что сделано
- [x] Прочитаны: план (Волна 0, D1/D2-ядро, Принципы), шаблон `RecoveryPerfTests.cs`,
      `Parser.Recovery.cs` (счётчики), `RecoveryEngine.cs` (S1..S5), `Rules.cs`, `Result.cs`,
      `FollowSetCalculator.cs` (GetTerminators), `TerminalComparer.cs`, `Injection.cs`.
- [x] Создан `Tests/ParserTests/Recovery/RecoveryCorpusTests.cs`: 7 сценариев D1_1..D1_7 +
      агрегированный отчёт `Test_D1_Corpus_Report` (7 строк в Trace).
- [x] Сборка: `dotnet build Tests/ParserTests/ParserTests.csproj` — успешно.
- [x] Тесты: `dotnet test Tests/ParserTests/ParserTests.csproj --filter "FullyQualifiedName~RecoveryCorpusTests"`
      — 8/8 зелёные. Полный ParserTests: 333 passed / 2 skipped / 0 failed (регрессий нет).

## Грамматики / входы
- D1.1: MiniC, N=120 функций, в каждой пропущена `;`.
- D1.2: MiniC, 500 корректных функций + 1 битая в конце.
- D1.3: вложенные циклы Module(ZeroOrMany)→Block(OneOrMany)→Statement, мусор `$` в начале
      итерации Statement. Без закрывающего `}` (иначе mismatch на `}` перезаписывает снимок).
- D1.4: regex-терминалы (Record = Ident ;), мусор `@` в середине.
- D1.5: longest-match (Item = Seq(a,b) | Seq(a)), мусор `$` в середине.
- D1.6: MiniC, 50 функций, ошибка в каждой 5-й (10 ошибок).
- D1.7: двойное повреждение (Construct = int Ident ;), `int a; @@@ int b $ c;`.

## Базовые числа (Wave 0 baseline, min-of-5 wall-time)
| Сценарий | Success@EOF | RecoveryPasses | EngineGenerateCalls | Memo.Count | resync |
|---|---|---|---|---|---|
| D1.1 | **False** | 65 | 0 | 463 | -1 |
| D1.2 | True | 1 | 0 | 3016 | -1 |
| D1.3 | True | 1 | 1 | 7 | 9 |
| D1.4 | True | 1 | 1 | 6 | 8 |
| D1.5 | True | 1 | 1 | 5 | 6 |
| D1.6 | True | 10 | 0 | 319 | -1 |
| D1.7 | True | 3 | 3 | 5 | **11** |

- D1.1: Success@EOF=False — ОЖИДАЕМО (MaxRecoveryIterations=64 < 120 ошибок). passes=65 =
  64 лимит + 1 начальный прогон. Это baseline для волны 1 (A4-2/A3: поднять лимит / профиль IDE).
- D1.2: passes=1 — префикс (500 функций) мемоизируется (memo=3016), S0 чинит единственную ошибку
  за один проход. Базовая метрика B1 (точная hygiene).
- D1.3: resync=9 — loop-level абсорбер (3.0c) пропустил мусор `[7..9)` и возобновился с чистого
  терминала. Ошибка в начале итерации (topIdx==0).
- D1.7: resync=11 — старт битой конструкции (`int b $ c;` начинается в pos 11). Метрика волны 5
  (A5-1): сейчас resync через S2/S3; после A5-1 сравнить, что resync не деградирует.

## Ключевые находки / отклонения
- **Бюджет попыток**: дефолт `MaxRecoveryAttemptsPerPosition=3` глушит S2/S3 (они — попытка 3+)
  в сценариях с несколькими S1-кандидатами. Для D1.3/D1.4/D1.7 выставлен явный бюджет 16
  (установленный паттерн, см. `Parser.Recovery.cs:39` и E2E-тесты). Это тестовая настройка
  инстанса, НЕ изменение продакшн-кода.
- **D1.3 грамматика**: закрывающий `}` (OftenMissed) перезаписывал снимок кадром Block (element `}`),
  из-за чего loop-level абсорбер не срабатывал. Убран `}` — тело внутреннего цикла теперь
  `OneOrMany(Ref(Statement))`, снимок попадает в Statement (topIdx==0).
- **ResyncPos** в отчёте = EndPos первого Skipped-диагноcтика (точка, куда пришёл recovery после
  первого пропуска), а не max EndPos — это корректная метрика для D1.7.
- **D1.1 ассерт** мягкий (`SuccessAtEof || Passes <= 2*n`): не падает из-за несделанной волны 1,
  но проверяет детерминированную границу проходов.

## Файлы
- Создан: `Tests/ParserTests/Recovery/RecoveryCorpusTests.cs`
- Создан: `docs/RecoveryImprovementPlan-progress0.1.md` (этот файл)
- Не изменены: продакшн-код `ExtensibleParser/`, `RecoveryPerfTests.cs`, прочие тесты.
