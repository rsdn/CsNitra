# Гайд автора грамматики: восстановление после ошибок

Краткое руководство по аннотациям `RecoveryOptions` (на `RecoveryRule`), семантике восстановленного дерева/диагностики и известным ограничениям. Полный план — `RecoverySystemPlan.v2.md` (§3.4, §3.6, §3.7, §3.8, §3.9, §5); статус — `RecoverySystemChecklist.md`.

## (а) Гайд автора грамматики — когда что аннотировать

Аннотации живут в `RecoveryOptions` и навешиваются на правило обёрткой `RecoveryRule(inner, options)` (`Rules.cs`). Поведение парсинга обёртки в точности равно `Inner`; опции читает только recovery-движок из кадра снимка (`StackFrame.Options`). Приоритет — **ближайший** кадр к упавшему элементу: поле, заданное в ближайшем кадре, переопределяет вычисленное (follow-set — только fallback); авторские цели либо используются, либо нет (без «комбинируем неконфликтующие»).

```csharp
Rules["Statement"] =
[
    new RecoveryRule(
        new Seq([new Literal("int"), Terminals.Ident(), new Literal(";")], "VarDecl"),
        new RecoveryOptions(
            TryInsert: [new Literal(";")],
            Terminators: [new Literal("}"), new Literal("else")])),
];
```

### `Terminators` — явные цели skip/completion
- **Что:** переопределяют вычисленный follow-set; цели S3 (panic-скан до терминатора) и S4 (достроение).
- **Когда:** вычисленный follow-set неточен/слишком широк — типично для **regex-терминалов** (First терминала = он сам, а regex матчит множество форм). Задайте короткие literals, до которых можно безопасно пропускать/достраивать.
- **Когда нет:** follow-set корректен (короткие literal-терминаторы) — не аннотируйте, движок возьмёт follow.
- **Пример:** `Terminators: [new Literal("}"), new Literal("else")]` на `Statement`.

### `Anchors` — структурные resync-якоря (S2, T1)
- **Что:** правила, позиция успешного полного спекулятивного parse которых = known-good точка продолжения (грамматика *доказывает* валидность, а не угадывает).
- **Когда:** нужно resync к известной валидной конструкции (следующий `Statement`/`Member`/`Function`).
- **Выводится:** если НЕ заданы — движок выводит якоря из элементов циклов в кадрах снимка (`ZeroOrMany(Ref("Statement"))` → `Statement`). Авторские расширяют/переопределяют выведенные.
- **Когда нет:** цикл уже даёт якорь (например top-level `Module`/`ZeroOrMany(Ref("Member"))` → `Member` выводится) — аннотация не нужна.
- **Пример:** `Anchors: [new Ref("Statement")]` на `Block`.

### `CanStart` — мягкие предикаты «может начаться» (S2, T2)
- **Что:** декларативный аналог Roslyn `IsPossibleStatement`/`CanStartMember`/`IsNamespaceMemberStartOrStop`. Допустимо грубее полного правила: отвечает «может ли здесь начаться конструкция» без требования разобрать её целиком.
- **Когда:** код после разлома **сам может быть бит** — полное правило (T1) на следующей конструкции не сработает, а мягкий предикат всё же найдёт точку продолжения.
- **Только авторские:** никогда не выводятся автоматически (их семантика не выводима из грамматики).
- **Пример:** `CanStart: [new Ref("MemberStart")]` на `Module` (мягкое: тип + ident, без требования к телу).

### `TryInsert` — терминалы для вставки (S1, ранг 0)
- **Что:** терминалы, которые движок может вставить при сбое этого правила (кандидаты ранга 0 — выше остальных источников S1).
- **Когда:** известный часто-пропускаемый токен (`;`, `}`, `)`).
- **Сахар:** `OftenMissed(t)` = `RecoveryRule(t, new RecoveryOptions(TryInsert: [t]))` (для `Terminal`).
- **Пример:** `TryInsert: [new Literal(";")]`.

### `MaxSkip` — лимит resync-скана (символы)
- **Что:** ограничение скана S2/S3 в символах (default 1000).
- **Когда:** узкий контекст, где resync далеко не имеет смысла / дорого.
- **Пример:** `MaxSkip: 5`.

### `Recoverable` — opt-out
- **Что:** `false` = правило не восстанавливается (строгие контексты). Движок игнорирует опции кадра с `Recoverable=false`; ε-принятие обёрнутой альтернативы отключается.
- **Когда:** конструкция, которую не стоит «чинить» (синтаксически критичный фрагмент).
- **Пример:** `new RecoveryOptions(Recoverable: false)`.

### Таблица: hand-written флаги Roslyn → декларативные `RecoveryOptions`

| Roslyn (hand-written) | `RecoveryOptions` (декларативно) |
|---|---|
| `TerminatorState` — 29-флаговая маска условий остановки, ~100 ручных save/restore `_termState` | `Terminators` (+ вычисленный follow-set как fallback) |
| hand-written предикаты `IsPossibleStatement` / `CanStartMember` / `IsNamespaceMemberStartOrStop` | `CanStart` (мягкие авторские правила) |
| правила-якоря / «следующая валидная конструкция» | `Anchors` (S2, T1; выводятся из циклов, если не заданы) |

## (б) Семантика восстановления — как выглядят дерево и диагностика

### Дерево
- Recovery-узлы помечены `IsRecovery = true` на самом узле (`Node`/`TerminalNode`, `SyntaxTree.cs`): **вставленный токен** = пустой `TerminalNode(…, ContentLength: 0, IsRecovery: true)`; **пропущенный текст (абсорбер)** = `TerminalNode("Skipped", e, S, S-e, IsRecovery: true)`. Текст абсорбера читается visitor'ом из `input` по позициям (дерево хранит только позиции).
- **I4 — ничего не теряется:** каждый символ входа входит ровно в один терминальный узел (обычный, вставленный или абсорбер). Тайлинг по `[StartPos, EndPos)`: хвостовой trivia поглощается в `EndPos` следующего терминала и отдельным узлом не представляется (`ContentLength < EndPos - StartPos`).
- Подсчёт recovery-узлов: `CostCalculator.CountRecoveryNodes(root)` (дешёвый обход по `IsRecovery`, без `Parser`).
- **I6 — безвредность:** на корректном коде (Success до EOF без recovery) дерево в точности совпадает с до-recovery (recovery латентен, 0 recovery-узлов).

### Диагностика

```csharp
public enum RecoveryKind { Inserted, Skipped, Unrecovered }

public sealed record RecoveryDiagnostic(
    int StartPos, int EndPos, RecoveryKind Kind,
    string Message, Terminal? Terminal, string? RuleName);

public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics { get; } // накапливается за все итерации
```

| Kind | Стратегия | Сообщение |
|---|---|---|
| `Inserted` | S1 (вставка ожидаемого) | `expected {kind}, found {preview}` |
| `Inserted` | S2 (нулевые вставки в resync-точке) | `insert at resync point {S}` |
| `Inserted` | S4 (достроение в EOF) | `insert {kind} at EOF` |
| `Skipped` | S2 (resync, `S > e`) | `skip to resync point {S}` |
| `Skipped` | S3 (panic до терминатора) | `skip to terminator {kind}` |
| `Skipped` | S5 (хвостовой мусор) | `trailing garbage: {n} chars` |

`Unrecovered` — в enum, но текущим движком не порождается (запасной). Одна диагностика на регион пропуска (остальное — молча, как у Roslyn).

### Финальные состояния (`Parse`, §3.6)

| Итог | Значение |
|---|---|
| Success, `NewPos == EOF` | чистый успех, `ErrorInfo = null` |
| Partial, `NewPos == EOF` | **восстановлено с дырами**: дерево полное (I4), `ErrorInfo = null`, дыры — в `RecoveryDiagnostics`/дереве |
| Success `< EOF` / Failure / Partial `< EOF` (после исчерпания кандидатов) | **невосстановлено**: `ErrorInfo = FatalError` в последней неотвратимой точке `e`, `RecoveryDiagnostics` — принятые восстановления до неё |

> Примечание (реализация, чек-лист 1.4): финальный Partial@EOF с непустой диагностикой недостижим — любой принятый кандидат достраивает дыру до `Success@EOF`. Partial@EOF как финал возникает, когда ни один кандидат не принят (diag пуст, дыры описаны Partial-узлами дерева).

### Приоритет кандидатов

Полная сортировка `(Rank, Cost, Pos, RuleName, TerminalKind)` (§3.4.3, I5 — детерминизм):

| Стратегия | Ранг | Что |
|---|---|---|
| **S0** | неявный, всегда первый | re-parse как есть: `RecoveryPrefix`/`RecoveryPostfix` + `OftenMissed` (инлайн, ε-принятие) — легаси-путь |
| **S1** | 0 / 1 | вставка терминала: `TryInsert` (ранг 0) / FailedTerminal·Expected·FollowSet (ранг 1) |
| **S2** | 2 | resync к known-good точке: T1 (якоря) / T2 (`CanStart`) |
| **S3** | 3 | panic-скан до токен-терминатора |
| **S4** | 4 | достроение в EOF (суффиксные обязательства) |
| **S5** | 5 | хвостовой мусор (абсорбер `[e..EOF)`) |

Акцепт кандидата — только при **E2 > E** (I1, прогресс), иначе откат.

**S0 > S2 (важно).** S0 — самый точный/дешёвый, всегда первый. Если S0 делает прогресс (ε-принял OftenMissed-терминал), S2 (T1/T2) **вообще не пробуется**. Следовательно, `Anchors`/`CanStart` (T1/T2) exercised **только** когда ошибка не чинится S0/S1 (не OftenMissed, не вставляемый терминал). Для E2E-тестов T1/T2 нужны входы, которые S0/S1 не чинят (миним. грамматика без OftenMissed). См. чек-лист 3.1.2b.

## (в) Известные ограничения

1. **Follow-set на regex-терминалах неточен.** First терминала = он сам, а regex матчит множество форм → цели skip неточные. Митигация: `Terminators` (переопределение автором), ограниченный скан (`MaxSkip`), неверный skip отклоняется механикой I1 (E2 не продвинулся → откат).
2. **Cost — эвристики** (`CostCalculator`): вставка = 1; skip = число небелых «слов» + число переводов строк; tier-penalty T1:0 / T2:1. Это сравнительная оценка, а не точная стоимость.
3. **S0 > S2 (T2 preemption)** — открытый дизайн-вопрос: не должен ли T2 (`CanStart`) иметь приоритет над S0 в отдельных случаях (напр. следующая конструкция — известный якорь)? S0 ε-принимает «лёгкий» OftenMissed, даже если resync к якорю дал бы чище дерево. См. план §5 / чек-лист 3.1.2b.
4. **`MaxRecoveryAttemptsPerPosition` (default 3) — предохранитель**, а не рабочий бюджет. S1 генерирует до `|FollowSet|+2` ≈ 9–12 кандидатов и выедает бюджет раньше, чем дойдут S2/S3/S5. Глубокие сценарии (resync/panic/trailing) ставят бюджет явно (E2E: `= 16`).
