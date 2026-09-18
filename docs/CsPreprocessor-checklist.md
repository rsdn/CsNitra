# CsPreprocessor — checklist

План: `docs/CsPreprocessor.md`. Один субагент на подпункт. Статусы:
`[ ]` не начат · `[~]` в работе (ровно один) · `[✅]` готово · `[❌]` провал.

- [✅] T0.1 Проект CsPreprocessor + типы + `Run` (заглушка)
- [✅] T0.2 Тестовый проект CsPreprocessorTests (smoke)
- [✅] T1.1 Грамматика: `PreprocessorFile`/`Line`/`CodeLine` (плитка всего текста)
- [✅] T1.2.1 Грамматика: декларативный `DirectiveLine` (`Ws* '#' Directive LineEnd`) + `Ws`/`LineEnd`/`Symbol` + простые директивы (Else/EndIf/Define/Undef) + `BadDirective` (catch-all). Плитка сохраняется
- [✅] T1.2.2 Грамматика: остальные директивы (If/Elif/Error/Warning/Line/Region/EndRegion/Pragma/Nullable/Shebang) + `Condition`-плейсхолдер для If/Elif
- [✅] T1.3 Грамматика: условие `#if`/`#elif` (TDOPP)
- [✅] T2.1 `DirectiveStack` (чистая логика): define/undef (активные), IsDefined (stack+cmdline), BranchTaken (первая истинная ветка), CompleteIf
- [✅] T2.2 `PreprocessorInterpreter` (visitor): обход в порядке исходника, active/inactive, сборка `Text` (same-length blanking)
- [✅] T2.3 Диагностика (`#error`/`#warning` активные + структурные) в координатах исходника
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
- Этап 2 пересобран: старое T2.1–T2.4 (visitor/semantics/blanking/diagnostics) имело
  пересечения. Новое: T2.1 = `DirectiveStack` (чистая логика, тестируется в изоляции),
  T2.2 = visitor (обход + active/inactive + blanking → Text), T2.3 = диагностика.
  Порядок: сначала логика символов, потом visitor, потом диагностика.
