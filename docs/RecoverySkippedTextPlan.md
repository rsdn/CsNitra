# Recovery: возврат пропущенного текста в предыдущий TerminalNode

**Статус:** продолжает `RecoverySystemPlan.v2.md` (Фазы 0–3 выполнены, см. `RecoverySystemChecklist.md`). Меняет **только представление финального дерева**; recovery-движок (генерация кандидатов, применение/откат патчей, диагностика) не трогается.

**Проблема, на которую отвечает план:** в восстановленном дереве пропущенный текст (грязь) — отдельный узел-абсорбер (`SkippedTerminal([24,28), «### »)`), стоящий самостоятельным элементом цикла/Seq. Исходный дизайн парсера: пропущенный текст закладывается в **предыдущий TerminalNode** — его `EndPos` смотрит за грязь, `ContentLength` задаёт длину актуальных данных, а `EndPos - StartPos - ContentLength` — длина грязи.

---

## 0. Контекст

### 0.1. Исходный дизайн (откуда требование)

1. **Mechanizm trivia уже реализует эту семантику** для белых символов: `ParseTerminal` (`Parser.cs:684-699`) — `EndPos` терминала включает хвостовой trivia, `ContentLength` — только сматченный контент. Для терминала `}` в `int foo() { return 0; }` (позиция 23) при следующем пробеле: `StartPos=23, EndPos=25, ContentLength=1`, разность `25-23-1=1` — длина пропущенного (белого) текста.
2. **План v1** (`docs/Old/RecoverySystemPlan.md:1128`) для recovery-грязи формулировал то же: «`TerminalNode` содержит только `StartPos`, `EndPos`, `ContentLength`. **`EndPos` может выходить за `StartPos + ContentLength`, покрывая пропущенный текст**» (там же, :1390: «его текст записывается в разницу между `EndPos` и `StartPos + ContentLength` предыдущего узла»).
3. **План v2** (§0 п.3, §3.8 `RecoverySystemPlan.v2.md:573`) отклонил v1-боковую таблицу `_skippedTextMap`, но выбрал другой вариант представления — **отдельный узел** `TerminalNode("Skipped", e, S, S-e, IsRecovery: true)` как полноправный участник дерева (аналог `SkippedTokensTrivia` Roslyn). Реализация (3.0c, чек-лист) поставила абсорберы на уровне цикла:

```
ModuleFunctions[0-51) = [ FunctionDecl[0-24), Skipped[24-28) «### » [REC], FunctionDecl[28-51) ]
```

Задача: вернуть представление «грязь в предыдущем терминале», сохранив весь recovery-механизм.

### 0.2. Почему нельзя встраивать грязь во время recovery (memo-патч)

Абсорбер создаётся как memo-патч: `Result.Success(absorberNode, S, S)` в ключ `(e, Rule)` (`RecoveryEngine.cs:174,181,456,472`; инъекция — `Injection.Absorb`, `RecoveryEngine.cs:606`). При ре-парсинге этот узел становится элементом цикла/Seq, а «предыдущий терминал» (напр. `}` позиции 23) живёт **внутри мемоизированного поддерева префикса**:

- узлы — неизменяемые `record`, общий экземпляр терминала `}` ссылают **многие** memo-записи `(0, Function)`, `(0, Member)`, `(0, Block)`…;
- переписывание префикса ломает **I2 (стабильность префикса)** и требует каскадной пересборки всего префикса + обновления всех ссылающихся memo-записей на каждой итерации.

⇒ Встраивание возможно только **после сборки финального дерева** — post-parse проходом, который применяется один раз к `Result`, возвращаемому `Parse`. К моменту его работы recovery-цикл завершён, memo больше не читается (следующий `Parse` начинает с `_memo.Clear()`, `Parser.cs:189`), поэтому проход не взаимодействует ни с I1, ни с I2, ни с откатами.

---

## 1. Целевая семантика

Для входа `int foo() { return 0; } ### int bar() { return 1; }` (len=51, тест `Test_BetweenFunctions_AbsorberAtLoopLevel_SuccessAtEof`):

```
ДО (сейчас):
ModuleFunctions[0-51) = [
    FunctionDecl[0-24)   (… Block[10-24) (… `}`[23-24) len=1 …)),
    Skipped[24-28) len=4 [REC],
    FunctionDecl[28-51)
]

ПОСЛЕ (цель):
ModuleFunctions[0-51) = [
    FunctionDecl[0-28)   (… Block[10-28) (… `}`[23-28) len=1 [REC] …)),
    FunctionDecl[28-51)
]
```

Правила целевого дерева:

1. **Грязь `[e..S)` встраивается в предыдущий TerminalNode** — последний терминал (в порядке документа) предыдущего элемента поддерева: `EndPos := S`, `ContentLength` не меняется, `IsRecovery := true`. Длина грязи = `EndPos - StartPos - ContentLength` (для `}`: `28-23-1 = 4`, регион грязи `[StartPos+ContentLength .. EndPos) = [24..28)` — совпадает с `RecoveryDiagnostic`).
2. **Цепочка предков хоста** (от терминала до корня поддерева-хоста включительно) получает расширенный `EndPos := S` — сохраняется инвариант «span составного узла = [первый StartPos .. последний EndPos] его детей» (в текущем дереве он вытекает из построения: `currentPos` наследуется от последнего дочернего терминала).
3. **Вставки (missing tokens, `ContentLength = 0`) не тронуты** — остаются нулевых-ширины recovery-узлами (аналог missing token Roslyn).
4. **`RecoveryDiagnostics` не меняются** — по-прежнему один `Skipped` на регион `[e..S)`; теперь этот регион равен избытку хоста над `ContentLength` (точнее: избыток ⊇ регион, т.к. в него может входить trivia между хостом и грязью — см. §7, риск R2).
5. **I4 (тайлинг `[StartPos, EndPos)` по терминалам) сохраняется**: спан абсорбера `[e..S)` переносится на хост, смежность гарантирует отсутствие дыр/наложений.
6. **I6 (безвредность) сохраняется до instance-идентичности**: нет абсорберов → проход возвращает тот же экземпляр дерева.

### 1.1. Что НЕ меняется (recovery-код)

- `RecoveryEngine` (S0–S5, порядок, cost, спекулятивные парсинги) — **побайтово**.
- Механика кандидатов: `Apply`/`Rollback`, `MemoPatch`-лог, `Hygiene`, акцепт `E2 > E` (I1), лимиты (I3) — **без изменений**.
- Дерево **в ходе** recovery (memo, промежуточные итерации, спекуляции) — без изменений; абсорбер-узлы по-прежнему создаются движком — они нужны механике memo-патчей. Fold — «презентационный» слой над финальным `Result`.
- `FinalizeResult`/`ErrorInfo` (семантика §3.6) — без изменений.
- Сборка без recovery (`Parser.NoRecovery.cs`) — без изменений в поведении (абсорберов нет; fold — no-op, см. §5).

---

## 2. Пометка абсорберов (предикат)

### 2.1. Проблема структурного предиката

Наивный предикат «`IsRecovery && ContentLength > 0 && ContentLength == EndPos - StartPos`» **некорректен**: `ErrorOperator` — `RecoveryTerminal` с regex `[\\\/*+\-<=>!@#$%^&]+` (`Tests/ParserTests/MiniC/Terminals.cs:17-18`) матчит **нелипший** текст (`x $^ 5`), узел получает `IsRecovery = true` (`Parser.cs:698`, `IsRecoveryTerminal`) и при отсутствии trivia удовлетворяет структурному предикату — fold сломал бы реальное совпадение.

### 2.2. Решение: явный маркер `IsAbsorber`

Поле `TerminalNode` (`SyntaxTree.cs:149`):

```csharp
public record TerminalNode(string Kind, int StartPos, int EndPos, int ContentLength,
    bool IsRecovery = false, bool IsAbsorber = false) : Node(Kind, StartPos, EndPos, IsRecovery)
```

- Параметр в конце с дефолтом — source-совместимо со всеми текущими местами создания.
- Ставится **только** в местах создания абсорберов:
  | Место | Что |
  |---|---|
  | `CreateInjectedResult` (`Parser.Recovery.cs:135-140`) | `IsAbsorber: injection.IsSkip && injection.Length > 0` (`Injection.IsSkip` уже существует, `Recovery/Injection.cs:10`) |
  | `RecoveryEngine.cs:174, 181` (S2) | `IsAbsorber: true` |
  | `RecoveryEngine.cs:456, 472` (S3) | `IsAbsorber: true` |
  | S5 (`RecoveryEngine.cs:606`) | через `Injection.Absorb("Trailing", …)` → покрывается `CreateInjectedResult` |
- **Не ставится**: реальные терминалы (`ParseTerminal`), вставки (`Length == 0`), `OftenMissed` (`ContentLength == 0`, `Parser.Recovery.cs:210`), `ErrorOperator`/`ErrorEmpty`.
- Альтернатива без изменения API (запасная, не рекомендуется): зарезервировать kind'ы `"Skipped"`/`"Trailing"` в предикате; требует задокументированного запрета называть пользовательские терминалы этими именами.

### 2.3. `CountRecoveryNodes` — без изменений

Считает `IsRecovery` (`Recovery/CostCalculator.cs`). После fold хост-терминал помечен `IsRecovery = true` ⇒ грязь по-прежнему видна в метрике; вставки и fallback-абсорберы — тоже. Семантика `IsRecovery` уточняется в документации: «узел создан **или расширен** при восстановлении» (хост расширен поглощением пропущенного региона).

---

## 3. Fold-проход (`SkippedTextFolder`)

Чистая функция над деревом: один левый-вправо проход, структурное шаринг (копируется только затронутая цепочка), **без аллокаций и с возвратом того же экземпляра, когда абсорберов нет** (I6 до instance-identity).

### 3.1. Правила

Обозначения: абсорбер `A = [e..S)` — `TerminalNode` с `IsAbsorber = true`. Хост `H` — ближайший предыдущий элемент (в порядке документа), **содержащий хотя бы один терминал**, при условии смежности: `EndPos` последнего терминала `H` равен `e` (следствие I4; нарушение ⇒ fallback).

| # | Случай | Действие |
|---|--------|----------|
| F1 | `H` — терминал | новый `TerminalNode(Kind, StartPos, EndPos = S, ContentLength, IsRecovery: true)` (маркер `IsAbsorber` у хоста **не** ставится — он не абсорбер, а носитель) |
| F2 | `H` — составной (`SeqNode`/`ListNode`/`SomeNode`) | рекурсивно: правый последний терминал в порядке документа; переписывается **только цепочка** от корня `H` до этого терминала (каждому узлу цепочки — `EndPos = S`), братья не копируются (reference-equality) |
| F3 | Абсорбер — первый элемент списка / предыдущие элементы не содержат терминалов (`NoneNode`) | **fallback**: абсорбер остаётся отдельным узлом (текущее поведение) |
| F4 | Последовательные абсорберы `[H, A1, A2, C]` | `A1` → в `H`; `A2` → в уже расширенный `H` (последний терминал `H` получает `EndPos = A2.EndPos`). Ведущий бег `[A1, A2, C]` (F3 для `A1`): `A2` складывается в `A1` ⇒ один абсорбер `[A1.start .. A2.end)` |
| F5 | Вставка (`ContentLength == 0`, `IsRecovery`) | не складывается; остаётся нулевых-ширины узлом |
| F6 | `ListNode` | порядок документа = чередование `e0, d0, e1, d1, …` (`Elements`/`Delimiters` — раздельные списки): fold работает над уплощённой последовательностью, на выходе оба списка восстанавливаются |
| F7 | Несмежные спаны (дыра/наложение в дереве, напр. Partial с дырами) | guard смежности не проходит ⇒ fallback F3 (не порекаем) |

### 3.2. Псевдокод

```csharp
// ExtensibleParser/SkippedTextFolder.cs — общий код (без #if RECOVERY), чистый, без Parser
internal static class SkippedTextFolder
{
    public static Result Fold(Result result)
    {
        if (!result.TryGetSuccess(out var node, out _) && !result.TryGetPartial(out node, out _))
            return result;                                  // Failure — узла нет
        var folded = FoldNode((Node)node!);
        return ReferenceEquals(folded, node) ? result : result.WithNode(folded);
    }

    static Node FoldNode(Node n) => n switch
    {
        SeqNode s     => FoldSeq(s),
        ListNode l    => FoldList(l),
        SomeNode so   => FoldSome(so),
        _             => n,                                 // TerminalNode / NoneNode / PredicateNode
    };

    static Node FoldSeq(SeqNode s)
    {
        var (els, changed) = FoldChildList(s.Elements);
        return changed
            ? new SeqNode(s.Kind, els, s.StartPos, s.EndPos)
            : s;
    }

    static Node FoldList(ListNode l)
    {
        var flat = Interleave(l.Elements, l.Delimiters);    // e0,d0,e1,d1,…
        var (flatOut, changed) = FoldChildList(flat);
        if (!changed) return l;
        var (els, dels) = Uninterleave(flatOut);
        return new ListNode(l.Kind, els, dels, l.StartPos, l.EndPos, l.HasTrailingSeparator, l.IsRecovery);
    }

    static Node FoldSome(SomeNode so)
    {
        var v = FoldNode((Node)so.Value);
        return ReferenceEquals(v, so.Value) ? so : new SomeNode(so.Kind, v, so.StartPos, so.EndPos);
    }

    // Левый-вправо: каждый абсорбер складывается в последний эмитированный
    // элемент, содержащий терминал (guard смежности), иначе — fallback.
    static (IReadOnlyList<Node> Out, bool Changed) FoldChildList(IReadOnlyList<ISyntaxNode> children)
    {
        var outList = new List<Node>(children.Count);
        var changed = false;
        foreach (var child in children)
        {
            var c = FoldNode((Node)child);
            if (c is TerminalNode { IsAbsorber: true } a)
            {
                var hostIdx = LastTerminalIndex(outList);   // справа-налево: индекс элемента с ≥1 терминалом
                if (hostIdx < 0 || LastTerminalOf(outList[hostIdx]).EndPos != a.StartPos)
                {
                    outList.Add(a);                          // F3/F7: standalone
                    continue;
                }
                outList[hostIdx] = ExtendLastTerminal(outList[hostIdx], a.EndPos);
                changed = true;
            }
            else
            {
                if (!ReferenceEquals(c, child)) changed = true;
                outList.Add(c);
            }
        }
        return (outList, changed);
    }

    // Цепочка правых последних терминалоносителей: EndPos = endPos у терминала (F1, +IsRecovery)
    // и у каждого составного узла цепочки (F2). Возвращает тот же экземпляр, если менять нечего.
    static Node ExtendLastTerminal(Node n, int endPos) => n switch
    {
        TerminalNode t => new TerminalNode(t.Kind, t.StartPos, endPos, t.ContentLength, IsRecovery: true),
        SeqNode s      => ExtendSeq(s, endPos),
        ListNode l     => ExtendList(l, endPos),
        SomeNode so    =>
        {
            var v = ExtendLastTerminal((Node)so.Value, endPos);
            return ReferenceEquals(v, so.Value) ? n : new SomeNode(so.Kind, v, so.StartPos, endPos);
        },
        _ => n,
    };
    // ExtendSeq/ExtendList: правый индекс элемента с ≥1 терминалом; рекурсия;
    // new SeqNode(Kind, elements[..i] + [newEl] + elements[(i+1)..], StartPos, Math.Max(EndPos, endPos))
    // (Math.Max — защита на дырчатых Partial-деревах; на корректном дереве endPos > EndPos всегда)
}
```

### 3.3. Свойства прохода

- **Чистота/детерминизм (I5):** без состояния; одно дерево ⇒ одно дерево.
- **I6:** нет `IsAbsorber`-узлов ⇒ `ReferenceEquals(folded, node)` ⇒ тот же `Result`.
- **I4:** мультимножество терминальных спанов сохраняется (спан абсорбера переносится на хост; guard смежности исключает дыры/наложения).
- **Позиции:** `Result.NewPos/MaxFailPos/ResultKind/Context` не меняются — заменяется только `Node`.
- **Idempotency:** `Fold(Fold(x))` идентично `Fold(x)` (после fold абсорберов нет).
- **Стоимость (нет полного пересбора):**
  - Сканирование: один проход O(размера дерева), **чтение** (проверка `IsAbsorber`); без аллокаций — новые списки/узлы создаются только в поддеревах, где найден абсорбер (флаг «changed» всплывает снизу; нетронутые поддеревья возвращаются тем же экземпляром).
  - Правка: на каждый регион грязи пересоздаётся **цепочка** — 1 терминал-хост + d составных узлов (d = вложенность от хост-терминала до корня поддерева-хоста; в примере §1: `}` → MultiBlock → FunctionDecl = 3) + 1 список элементов родителя. Остальные поддеревья — shared by reference.
  - Итого: **O(k·d)** новых узлов (k = число регионов грязи), а не O(дерева). Ориентир: 1M узлов / 100 регионов грязи → ~500–1000 новых record (≈0,1%); память финального дерева — нейтрально/чуть меньше (абсорберы исключены).

---

## 4. Интеграция в `Parser`

### 4.1. Точка вставки

`Parser.Parse` (`Parser.cs:182-209`):

```csharp
var result = Recover(input, startRule, currentStartPos);
result = PresentTree(result);                               // НОВОЕ: до early-return
if (result.TryGetSuccess(out _, out var end) && end == input.Length)
    return result;
FinalizeResult(result, input);
return result;
```

**O(1)-гейт (важно для производительности):** fold вызывается только если recovery что-то принял. `PresentTree` — partial-хук (паттерн `Recover`/`FinalizeResult`):

```csharp
// Parser.Recovery.cs
private partial Result PresentTree(Result result) =>
    _recoveryDiagnostics.Count > 0 ? SkippedTextFolder.Fold(result) : result;

// Parser.NoRecovery.cs
private partial Result PresentTree(Result result) => result;
```

- **Строгость импликации:** узел с `IsAbsorber` может появиться в дереве только через `Apply` принятого кандидата (S2/S3 memo-патч или S3/S5 инъекция); каждый такой кандидат несёт ≥1 `Skipped`-диагностику, а диагностика добавляется в `_recoveryDiagnostics` только при акцепте. Значит: `Count == 0` ⇒ абсорберов нет ⇒ fold не нужен. (Обратная импликация не обязательна: при вставках-только fold сделает полный проход и вернёт тот же экземпляр.)
- **Следствие:** на корректном коде (и вообще без recovery) оверхед = **ноль** — ни прохода, ни аллокаций (I6 до instance-identity сохраняется тривиально).
- Применяется к **Success и Partial** (оба несут дерево; Partial<EOF — невосстановленное дерево тоже может содержать принятые ранее абсорберы).
- Вызывается **после** завершения recovery-цикла: memo на этот момент не читается (в `Recover` после финального `return result` цикл завершён; следующий `Parse` чистит `_memo`).

### 4.2. `Result.WithNode`

`Result.cs` (рядом с существующим `WithPrefixOnly`, `Result.cs:91`):

```csharp
public Result WithNode(ISyntaxNode node) => new(ResultKind, node, NewPos, MaxFailPos, Context);
```

### 4.3. Известные несоответствия (документировать, не чинить)

- `Parser.Memo` / `MemoizationVisualazer` после `Parse` показывают **до-fold** узлы (memo не переписывается) — debug-only; тесты, инспектирующие memo (`PatchRollbackTests`), сравнивают memo с memo и не затрагиваются.
- `Node.Debug()`/`DebugContent()` хоста показывают расширенный спан (контент + trivia + грязь) — ожидаемо для отладки; `TerminalNode.AsSpan`/`ToString(input)` по-прежнему используют `ContentLength` — visitor получает чистые данные.

---

## 5. Сборка без recovery

`Parser.NoRecovery.cs`: хук `PresentTree` — тривиальный (возврат `result` без прохода, см. §4.1); абсорберы не порождаются (инъекций/патчей нет), fold на таком дереве в любом случае был бы no-op. `SkippedTextFolder.cs` и поле `IsAbsorber` — общий код без `#if RECOVERY` (как `Injection.cs`/`CostCalculator.cs`): семантика дерева («пропущенный текст в предыдущем терминале») — свойство дерева, а не recovery-режима.

---

## 6. Фазы и тесты

Каждый пункт — отдельный коммит; регрессия после каждого пункта: `dotnet build Nitra.sln --no-incremental` → 0/0, `dotnet test Tests/ParserTests` зелёный.

### 4.1 Маркер `IsAbsorber` (без изменения поведения)
- **Файлы:** `SyntaxTree.cs` (поле), `Parser.Recovery.cs:135` (`CreateInjectedResult`), `RecoveryEngine.cs:174,181,456,472`.
- **Тесты** (новые, `Tests/ParserTests/Recovery/SkippedTextMarkerTests.cs`):
  1. between-функции (`AbsorberPlacementTests`-вход) → ровно 1 узел с `IsAbsorber` (абсорбер пока standalone).
  2. true-EOF вход → ровно 1 `IsAbsorber`.
  3. `z = x $^ 5;` (RecoveryOperator, MiniC) → терминал `ErrorOperator` в дереве: `IsRecovery == true`, `IsAbsorber == false` (защита от ложного срабатывания структурного предиката).
  4. пропущенная `;` (OftenMissed-вставка) → вставленный узел: `IsAbsorber == false`.
  5. корректный код → 0 узлов с `IsAbsorber` (I6).
- **Критерий:** дерево/результаты/диагностика на всех входах не изменились (только добавлено поле).

### 4.2 `SkippedTextFolder` — чистые unit-тесты (без Parser)
- **Файл:** `ExtensibleParser/SkippedTextFolder.cs`.
- **Тесты** (`Tests/ParserTests/Recovery/SkippedTextFoldTests.cs`, синтетические деревья):
  1. F1: `[T(23,24,len=1), A(24,28)]` → `[T(23,28,len=1,IsRecovery=true)]`; `IsAbsorber` у хоста false.
  2. F2: хост-SeqNode `[… T(23,24)]`: переписана только цепочка (братья — reference-equality), `EndPos` составных узлов цепочки = 28.
  3. F3: абсорбер первым → standalone (тот же экземпляр).
  4. F3: `[NoneNode, A]` → host ищется дальше; `[NoneNode, A]` без терминалов вовсе → standalone.
  5. F4: `[H, A1(24,28), A2(28,32), C]` → последний терминал H: `EndPos=32`; `[A1, A2, C]` → один абсорбер `(A1.start, 32)`.
  6. F5: вставка `(24,24,len=0,IsRecovery)` перед абсорбером — остаётся; абсорбер складывается в неё (последний терминал = вставка) — поведение зафиксировано тестом.
  7. F6: `ListNode` — абсорбер после delimiter; host = последний терминал в чередовании; `Elements`/`Delimiters` восстановлены.
  8. F7: дыра (предыдущий терминал `EndPos < A.StartPos`) → standalone, дерево не порвано.
  9. I6: дерево без абсорберов → `ReferenceEquals(Fold(r).Node, r.Node)` и тот же `Result`.
  10. Idempotency: `Fold(Fold(r))` == `Fold(r)`.
  11. I4: спаны терминалов до/после fold тайлят `[0, len)` (переиспользовать логику `SpansTileInput`, `MiniCEndToEndTests.cs:330`).
  12. Детерминизм: два прохода → идентичная каноническая форма.
- **Критерий:** все 12+ тестов зелёные; Parser не затронут.

### 4.3 `Result.WithNode`
- **Файлы:** `Result.cs`.
- **Тесты:** `WithNode` сохраняет kind/позиции/Context, заменяет Node (1–2 ассерта в `SkippedTextFoldTests` или `ResultTests`).

### 4.4 Интеграция в `Parse` + обновление существующих тестов
- **Файлы:** `Parser.cs` (2 строки в `Parse`).
- **Обновление ассертов:**
  - `AbsorberPlacementTests.Test_BetweenFunctions_AbsorberAtLoopLevel_SuccessAtEof`: Success@EOF, `ErrorInfo == null`, диагностика `Skipped [24..28)` — **без изменений**; добавить: в дереве **нет** узла с `IsAbsorber`; последний терминал поддерева foo (`}`) — `StartPos=23, EndPos=28, ContentLength=1, IsRecovery=true`; `28-23-1 == 4`; `FunctionDecl` foo — `EndPos=28`.
  - `AbsorberPlacementTests.Test_TrueEof_AbsorberAtLoopLevel_SuccessAtEof`: `}` — `[23..27)`, `ContentLength=1`, грязь `= 3`.
  - `MiniCEndToEndTests.Test_TrailingGarbage` (3.1.1): аналогично (последний терминал файла расширен; `CountRecoveryNodes >= 1` — хост помечен).
  - `Test_EverythingRepresentedInTree` (I4), `Test_Deterministic` (I5), `Test_Recovery_DoesNotBreakCorrectCode` (I6) — **проходят без изменений** (I5-дерево меняется относительно HEAD — ожидаемо, каноническая форма детерминирована).
  - `Test_MissingClosingBrace_Function` / `Test_MissingSemicolon_MultipleStatements` — ассерты `CountRecoveryNodes` остаются истинными (вставки/хосты помечены).
- **Новый E2E (опционально):** ведущая грязь `### int foo() { return 0; }` — если recovery даёт Success@EOF, абсорбер standalone (первый элемент, F3); ассерт — инвариант «нет хоста ⇒ standalone», не конкретный исход.

### 4.5 Регрессия и производительность
- Полный прогон: `ParserTests` + `RegexTests` + `WiWorkflowTests` — зелёные; сборка 0/0.
- `RecoveryPerfTests` — замер до/после по двум классам входов:
  - **Чистые** (N функций, корректный код): ожидаемый оверхед = **0** (гейт §4.1: `RecoveryDiagnostics.Count == 0` ⇒ `Fold` не вызывается; ни проход, ни аллокации). Критерий: время и аллокации в пределах шума (±2%).
  - **Грязные** (N ошибок, текущий бенчмарк): fold = один O(дерева) проход чтения + O(k·d) аллокаций (§3.3); относительно стоимости самих recovery-проходов (N re-parse) — пренебрежимо. Критерий: деградация ≤ 2%.
  - При нарушении критериев — диагностика (профиль аллокаций), а не отмена плана: ожидаемый «горячий» участок — проход чтения по дереву, который при необходимости сворачивается счётчиком «абсорбер создан» (инкремент в `Apply` принятых кандидатов), сужающим гейт до точного.

### 4.6 Документация
- `docs/RecoveryAuthorGuide.md` §(б):
  - «Дерево»: заменить пункт «пропущенный текст (абсорбер) = `TerminalNode("Skipped", e, S, S-e, IsRecovery: true)`» на целевую семантику (§1): грязь в предыдущем терминале (`EndPos`/`ContentLength`), цепочка предков, fallback (F3), вставки — нулевых ширины узлами; уточнить `IsRecovery` («создан или расширен при восстановлении»).
  - «I4» — пометка: спан абсорбера переносится на хост, тайлинг терминальных спанов сохраняется; избыток `EndPos - StartPos - ContentLength` = trivia + грязь (точный регион грязи — в `RecoveryDiagnostics`).
  - «Чистота дерева для AST»: в финальном дереве **нет узлов, не предусмотренных грамматикой** — только терминалы и структурные узлы грамматических правил; исключения — нулевые вставки (маркеры дыр, аналог missing token Roslyn) и fallback-абсорбер (F3, грязь без предыдущего терминала). Грязь детектируется visitor'ом по `IsRecovery && EndPos - StartPos > ContentLength` — отдельный тип/ветка для абсорбера в анализе дерева не нужна.
- `docs/RecoverySystemChecklist.md`: новый раздел «Фаза 4. Возврат пропущенного текста в предыдущий TerminalNode», пункты 4.1–4.6 (статус `[ ]`).

---

## 7. Риски

| # | Риск | Влияние | Митигация |
|---|------|---------|-----------|
| R1 | `ErrorOperator`/реальные `RecoveryTerminal`-совпадения путаются с абсорберами структурным предикатом | fold сломаёт дерево реального кода (`x $^ 5`) | явный маркер `IsAbsorber` (§2.2) + тест 4.1.3 |
| R2 | В избытке хоста trivia и грязь неразличимы по структуре (`EndPos - StartPos - ContentLength` = trivia + грязь) | visitor не может по одному узлу отделить белое от мусора | точный регион грязи — в `RecoveryDiagnostics` (один `Skipped` на регион); задокументировать; trivia — тот же механизм по исходному дизайну (§0.1) |
| R3 | Потребители, ожидающие «абсорбер = отдельный узел» (внешние visitor'ы) | изменение формы дерева на повреждённых входах | API не ломается (терминал остаётся `TerminalNode`); I4/I5/I6 сохранены; документация §(б); на корректном коде дерево идентично (I6) |
| R4 | Partial-дерево с дырами (тайлинг нарушен) | fold может «перепрыгнуть» через дыру | guard смежности (F7): нет смежного терминала-хоста ⇒ standalone, дерево не меняется |
| R5 | Memo/debug-view после `Parse` показывают до-fold дерево | рассогласование «memo ≠ финальный Result» | debug-only, задокументировать (§4.3); memo чистится следующим `Parse` |
| R6 | Одинокий абсорбер без хоста (грязь в начале входа) остаётся отдельным узлом | частичное возвращение старого поведения | fallback F3 — осознанный; единственный случай без предыдущего терминала |

---

## 8. Финальный критерий приёмки

Для `Test_BetweenFunctions_AbsorberAtLoopLevel_SuccessAtEof` (вход `int foo() { return 0; } ### int bar() { return 1; }`):

```
Result: Success@51, ErrorInfo == null
RecoveryDiagnostics: [ Skipped [24..28) "skip to resync point 28" ]   (без изменений)
Дерево:
  ModuleFunctions[0-51)
  ├─ FunctionDecl[0-28)      ← EndPos расширен (было 24)
  │   └─ … Block[10-28)      ← цепочка предков
  │       └─ `}` TerminalNode[23-28) len=1 [REC]   ← EndPos смотрит за грязь
  │            грязь = 28 - 23 - 1 = 4  («### », регион [24..28) == диагностике)
  └─ FunctionDecl[28-51)
В дереве нет узлов с IsAbsorber; CountRecoveryNodes == 1 (хост `}`).
```

Плюс: полный регрессионный прогон зелёный, сборка `Nitra.sln --no-incremental` — 0 ошибок / 0 предупреждений.
