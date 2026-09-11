# Recovery System Implementation Checklist

## Фаза 0: Исследование и инфраструктура диагностики

- [x] **0.1** Расширение FollowSetCalculator
  - Переписать `FlattenRule`: SeparatedList, ReqRef, OneOrMany, ZeroOrMany, Optional
  - Terminal identity: Literal по Value, остальные по инстансу
  - Конфигурируемый start symbol
  - Nested follow-sets
  - Тесты: `FollowSetTests.cs`

- [ ] **0.2** Контекст восстановления в Result.Partial [ЗН-5]
  - nullable `ParseContext?` в `Result` struct
  - `ParseContext`, `ParseLocation`, `SeqLocation`, `LoopLocation`, `PrefixLocation`
  - `RecoveryStackReconstructor`
  - Модификация Parser.cs — создание Partial с Context
  - Тесты: `ParseContextTests.cs`

- [ ] **0.3** Представление ошибок в дереве [ЗН-4]
  - `_skippedTextMap` в Parser
  - `GetSkippedText`, `RegisterSkippedText`
  - `RecoveryDiagnostic`
  - `RecoveryTreeExtensions`
  - Тесты: `ErrorRepresentationTests.cs`

## Фаза 1: Мемо-инъекция и итеративное восстановление

- [ ] **1.1** Алгоритм итеративного восстановления [ЗН-1,2,3,7,11]
  - `_terminalMemo`, `_isRecoveryMode`
  - `IsRecoveryPatch` в Context
  - Изоляция предикатов
  - `CleanMemoForRecovery`
  - Инвариант прогресса
  - `RecoveryEngine`, `MemoPatch`, `RecoveryContextSnapshot`
  - Миграция старого recovery-кода
  - Тесты: `IterativeRecoveryTests.cs`

- [ ] **1.2** Стратегия 1: Вставка ожидаемого токена
  - `InsertTokenStrategy`
  - `ModifyPartialForContinuation`
  - Тесты: `InsertTokenTests.cs`

- [ ] **1.3** Стратегия 2: Пропуск текста
  - `SkipAheadStrategy`
  - `CachedTryMatch`
  - Тесты: `SkipAheadTests.cs`

- [ ] **1.4** OftenMissed — улучшение
  - Интеграция с memo-патчами
  - Тесты: `OftenMissedTests.cs`

## Фаза 2: Multi-path exploration с cost-based выбором

- [ ] **2.1** Recovery Candidate и Cost Model
  - `MemoPatch`, `CostCalculator`
  - Cost comparison в Parser.ParseRule
  - Тесты: `CostModelTests.cs`

- [ ] **2.2** Генерация кандидатов на восстановление
  - `GenerateMemoPatches`, `SelectBestPatchSet`
  - Тесты: `CandidateGenerationTests.cs`

- [ ] **2.3** Rollback с вставкой из Follow-Set
  - `RollbackWithInsertionStrategy`
  - Тесты: `RollbackTests.cs`

## Фаза 3: Представление пропущенного текста в дереве

- [ ] **3.1** Пропущенный текст во внешней хэш-таблице
  - `_skippedTextMap`, `RecoveryTreeExtensions`
  - Тесты: `RecoveryTreeTests.cs`

- [ ] **3.2** Diagnostics collection
  - `RecoveryDiagnostics` свойство в Parser
  - Тесты: `DiagnosticsTests.cs`

## Фаза 4: Пользовательские расширения через аннотации грамматики

- [ ] **4.1** RecoveryRule — обёртка правила с аннотациями
  - `RecoveryRule`, `RecoveryOptions`
  - Интеграция с RecoveryEngine
  - Тесты: `RecoveryRuleTests.cs`

- [ ] **4.2** TerminatorPredicate — предикат терминатора
  - `TerminatorPredicate`
  - Тесты: `TerminatorPredicateTests.cs`

- [ ] **4.3** Integration с существующими механизмами
  - OftenMissed → RecoveryRule
  - RecoveryTerminal → Trivia absorption

## Фаза 5: Финальная интеграция и полировка

- [ ] **5.1** Longest-match с recovery-aware scoring
  - `IsBetterResult`
  - `CleanPreferenceThreshold`
  - Тесты: `LongestMatchRecoveryTests.cs`

- [ ] **5.2** End-to-end тесты
  - Комплексные тесты на MiniC
  - `EndToEndTests.cs`

- [ ] **5.3** API обобщения
  - Финальный публичный API
