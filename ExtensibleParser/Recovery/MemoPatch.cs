namespace ExtensibleParser.Recovery;

/// <summary>
/// Патч таблицы memo: ключ и предыдущее значение (null — ключа не было).
/// Откат возвращает OldValue (или удаляет ключ, если OldValue == null).
/// </summary>
public readonly record struct MemoPatch((int Pos, string Rule, int Precedence) Key, Result? OldValue);

/// <summary>
/// Патч таблицы инъекций: ключ и предыдущее значение (null — ключа не было).
/// Откат возвращает OldValue (или удаляет ключ, если OldValue == null).
/// </summary>
public readonly record struct InjectionPatch((int Pos, Terminal Terminal) Key, Injection? OldValue);

/// <summary>
/// Лог патчей текущего кандидата: memo- и инъекционные патчи. Основа отката (RollbackPatches).
/// </summary>
public readonly record struct PatchLog(List<MemoPatch> Memo, List<InjectionPatch> Injection);
