namespace ExtensibleParser.Recovery;

/// <summary>
/// Патч таблицы memo/инъекций: ссылка на таблицу, ключ, предыдущее значение (null — ключа не было)
/// и новое (null — удаление ключа). Откат возвращает OldValue (или удаляет ключ, если OldValue == null).
/// </summary>
public readonly record struct MemoPatch(object Table, object Key, object? OldValue, object? NewValue);
