#if NETSTANDARD

namespace System;

public readonly struct Index : IEquatable<Index>
{
    private readonly int _value;

    // Создаёт индекс из начала (isFromEnd = false) или конца (isFromEnd = true)
    public Index(int value, bool isFromEnd)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");

        _value = value;
        IsFromEnd = isFromEnd;
    }

    // Создаёт индекс относительно начала
    public static Index Start => new Index(0, false);

    // Создаёт индекс относительно конца
    public static Index End => new Index(0, true);

    // Получает значение индекса
    public int Value => _value;

    // Определяет, отсчитывается ли индекс от конца
    public bool IsFromEnd { get; }

    // Создаёт индекс из целого числа (от конца ^x)
    public static Index FromEnd(int value) => new Index(value, true);

    // Создаёт индекс от начала
    public static Index FromStart(int value) => new Index(value, false);

    public int GetOffset(int length) => IsFromEnd ? length - _value >= 0 ? length - _value : 0 : _value;

    // Реализация Equals
    public override bool Equals(object obj) =>
        obj is Index index && Equals(index);

    public bool Equals(Index other) =>
        _value == other._value && IsFromEnd == other.IsFromEnd;

    public override int GetHashCode() => (_value * 397) ^ IsFromEnd.GetHashCode();

    public override string ToString() =>
        IsFromEnd ? $"^{_value}" : _value.ToString();

    public static bool operator ==(Index left, Index right) => left.Equals(right);
    public static bool operator !=(Index left, Index right) => !left.Equals(right);
}

#endif
