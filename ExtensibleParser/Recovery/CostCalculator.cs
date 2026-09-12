namespace ExtensibleParser.Recovery;

/// <summary>
/// Единый cost model восстановления (§3.4, план v2 §0 п.7): вставка = 1; skip = число
/// небелых «слов» (максимальных небелых прогонов) + число переводов строк в пропущенном
/// регионе; tier-penalty T1 = 0, T2 = 1. Чистые расчёты без зависимостей от Parser.
/// </summary>
public static class CostCalculator
{
    /// <summary>Стоимость одной вставки (missing token).</summary>
    public const int InsertCost = 1;

    /// <summary>Стоимость skip-региона: число небелых «слов» + число переводов строк в [from..to).</summary>
    public static int SkipCost(string input, int from, int to)
    {
        var words = 0;
        var newlines = 0;
        var inWord = false;
        for (var i = from; i < to; i++)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                if (c is '\n' or '\r')
                    newlines++;
                inWord = false;
            }
            else if (!inWord)
            {
                words++;
                inWord = true;
            }
        }
        return words + newlines;
    }

    /// <summary>Tier-penalty: T1 = 0, T2 = 1.</summary>
    public static int TierPenalty(string tier) => tier switch
    {
        "T1" => 0,
        "T2" => 1,
        _ => 1
    };

    /// <summary>Число узлов дерева с IsRecovery == true (вставленные токены + абсорберы).</summary>
    public static int CountRecoveryNodes(Node root)
    {
        static int Count(ISyntaxNode node)
        {
            var count = node.IsRecovery ? 1 : 0;
            switch (node)
            {
                case SeqNode seq:
                    foreach (var el in seq.Elements)
                        count += Count(el);
                    break;
                case ListNode list:
                    foreach (var el in list.Elements)
                        count += Count(el);
                    foreach (var d in list.Delimiters)
                        count += Count(d);
                    break;
                case SomeNode some:
                    count += Count(some.Value);
                    break;
            }
            return count;
        }
        return Count(root);
    }
}
