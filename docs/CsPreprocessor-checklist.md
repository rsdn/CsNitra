# CsPreprocessor — checklist

План: `docs/CsPreprocessor.md`. Один субагент на подпункт. Статусы:
`[ ]` не начат · `[~]` в работе (ровно один) · `[✅]` готово · `[❌]` провал.

- [✅] T0.1 Проект CsPreprocessor + типы + `Run` (заглушка)
- [✅] T0.2 Тестовый проект CsPreprocessorTests (smoke)
- [✅] T1.1 Грамматика: `PreprocessorFile`/`Line`/`CodeLine` (плитка всего текста)
- [✅] T1.2.1 Грамматика: декларативный `DirectiveLine` (`Ws* '#' Directive LineEnd`) + `Ws`/`LineEnd`/`Symbol` + простые директивы (Else/EndIf/Define/Undef) + `BadDirective` (catch-all). Плитка сохраняется
- [✅] T1.2.2 Грамматика: остальные директивы (If/Elif/Error/Warning/Line/Region/EndRegion/Pragma/Nullable/Shebang) + `Condition`-плейсхолдер для If/Elif
- [ ] T1.3 Грамматика: условие `#if`/`#elif` (TDOPP)
- [ ] T2.1 Интерпретатор (visitor): каркас stack + active/inactive + сборка `Text`
- [ ] T2.2 Семантика символов (define/undef/IsDefined/BranchTaken/CompleteIf)
- [ ] T2.3 Active/inactive + same-length blanking
- [ ] T2.4 Диагностика (`#error`/`#warning` + структурные)
- [ ] T3.1 Тесты сохранности позиций (отдельные)
- [ ] T3.2 Интеграционный тест: препроцессор → CSharpParser, позиции совпадают
- [ ] T4.1 Вложенные/несоответствующие `#if`/`#endif`/`#else`
- [ ] T4.2 Неактивные `#define`/`#undef`/`#error`/`#warning` без эффекта
- [ ] T4.3 `#line` (запоминание в `LineDirectives`)
- [ ] T4.4 Multi-line string/comment с line-start `#`
- [ ] T4.5 Shebang / CRLF / ws перед `#` / bad placement
- [ ] T5.1 Прогон по реальным .cs-файлам
- [ ] T5.2 Производительность (при необходимости)

## Deviations

- T1.2 (план) расщеплён на T1.2.1 + T1.2.2: одна сессия субагента на 15+ директив +
  терминалы ушла бы в предел контекста. T1.2.1 — декларативная структура + простые
  директивы + catch-all; T1.2.2 — остальные директивы + плейсхолдер условия.
