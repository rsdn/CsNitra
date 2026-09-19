# RecoveryImprovementPlan — 5a.5 progress

## 5a.5.1: кэш First (`_firstCache` + reference-компаратор)

Статус: **done** (инфраструктура; без нового теста — проверяется в 5a.5.2).

### Что добавлено

Всё в одном файле: `ExtensibleParser/Parser.cs`.

1. **Вложенный `ReferenceComparer`** (`Parser.cs:48-72`) — `private sealed class ReferenceComparer : IEqualityComparer<Rule>`:
   - `static readonly ReferenceComparer Instance`;
   - `Equals` = `ReferenceEquals(x, y)`;
   - `GetHashCode` — identity-хэш (BCL `RuntimeHelpers.GetHashCode`).
2. **Поле `_firstCache`** (`Parser.cs:46`) — `private readonly Dictionary<Rule, Terminal[]> _firstCache = new(ReferenceComparer.Instance);` (рядом с `_terminalCache`, ключ — ссылка на `Rule`).
3. **Сброс в `Parse`** (`Parser.cs:233`) — `_firstCache.Clear();` сразу после `_terminalCache.Clear();` (рядом с `_memo.Clear()`).

### Deviation (важно)

Спецификация давала литерально `System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj)`. В этой кодовой базе **не компилируется**: `Shared/NetStandard2_0Support.cs` определяет локальный `internal static partial class System.Runtime.CompilerServices.RuntimeHelpers` (в `ExtensibleParser` через shared-import), который **по имени перекрывает** BCL-тип (CS0436), и у него нет `GetHashCode(object)` (CS1501). `ReferenceEqualityComparer` недоступен (netstandard2.0).

Решение (один файл, behavior-идентично спецификации): identity-хэш вызывается тем же BCL-методом `RuntimeHelpers.GetHashCode(object)`, но через **делегат**, метод которого резолвится по сборке (`typeof(object).Assembly.GetType("System.Runtime.CompilerServices.RuntimeHelpers")` → `GetMethod("GetHashCode", [typeof(object)])` → `Delegate.CreateDelegate`), минуя C# name resolution (рефлексия один раз, в статичном конструкторе). Fallback на `o.GetHashCode()`, если метод не найден (на .NET 8 не срабатывает). Результат: та же reference-equality + тот же identity-хэш, что требует спецификация; один продакшн-файл, `.csproj` не тронут.

### Файлы изменены

- `ExtensibleParser/Parser.cs` — добавлены `_firstCache` (поле), `ReferenceComparer` (вложенный класс), `_firstCache.Clear()` в `Parse`.

### Тесты

- Новый тест: **нет** (инфраструктура).
- `dotnet build Tests/ParserTests/ParserTests.csproj` → **0 errors**, 0 warnings.
- `dotnet test Tests/ParserTests/ParserTests.csproj` → **359 passed / 0 failed / 2 skipped** (совпадает с baseline, без регрессии).

### Не тронуто

`ParseTerminal`, `ParseRule`, S1–S6, `ReportMismatch`, `.csproj` — не менялись. Коммит не выполнялся.
