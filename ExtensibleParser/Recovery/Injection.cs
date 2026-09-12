namespace ExtensibleParser.Recovery;

/// <summary>
/// Инъекция engine'а: вставленный (Length == 0) или абсорбирующий (Length > 0) терминал
/// в конкретной позиции. Проверяется ПЕРЕД терминальным кэшем.
/// </summary>
/// <param name="Length">Длина совпадения: 0 — вставка (missing token), &gt; 0 — абсорбер (skip).</param>
/// <param name="NodeKind">Kind создаваемого TerminalNode.</param>
/// <param name="IsSkip">True — абсорбер (пропуск региона), false — вставка.</param>
public readonly record struct Injection(int Length, string NodeKind, bool IsSkip)
{
    public static Injection Insert(string kind) => new(0, kind, false);
    public static Injection Absorb(string kind, int length) => new(length, kind, true);
}
