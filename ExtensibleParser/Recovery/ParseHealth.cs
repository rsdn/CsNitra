namespace ExtensibleParser.Recovery;

/// <summary>
/// Вердикт о здоровье разбора: <see cref="Clean"/> — без восстановления;
/// <see cref="Recovered"/> — успех достигнут с восстановлением;
/// <see cref="FalseGreen"/> — Success@EOF, но более половины входа поглощено мусором;
/// <see cref="Failed"/> — парсер не дошёл до конца входа;
/// <see cref="Uncovered"/> — Success@EOF, но терминальные спаны дерева не покрывают вход
/// (нарушен инвариант I4: разбор заявляет успех, не покрывая вход).
/// </summary>
public enum ParseVerdict
{
    Clean,
    Recovered,
    FalseGreen,
    Failed,
    Uncovered
}

/// <summary>
/// Итоговый срез здоровья разбора: успех на EOF, объём поглощённого мусора в байтах
/// и в процентах от входа, число recovery-узлов, признак покрытия входа терминальными
/// спанами дерева (инвариант I4) и вердикт.
/// </summary>
public sealed record ParseHealthReport(
    bool SuccessAtEof,
    int AbsorberBytes,
    int AbsorberFraction,
    int RecoveryNodes,
    bool CoversInput,
    ParseVerdict Verdict);

/// <summary>
/// Единый источник истины о «здоровье» разбора: по дереву и длине входа считает объём
/// абсорберов, recovery-узлы, покрытие входа и выносит вердикт. FalseGreen — это Success@EOF,
/// достигнутый поглощением более половины входа как мусора. Инвариант покрытия (I4):
/// терминальные спаны дерева должны покрывать вход без пропусков и наложений;
/// Uncovered ловит Success@EOF при пустом/неполном дереве (memo-патч без реального разбора).
/// </summary>
public static class ParseHealth
{
    /// <summary>
    /// Анализирует дерево разбора: байты абсорберов, их долю от входа (в процентах),
    /// число recovery-узлов, покрытие входа терминальными спанами (инвариант I4)
    /// и вердикт в порядке Failed → Uncovered → FalseGreen → Clean → Recovered.
    /// </summary>
    public static ParseHealthReport Analyze(ISyntaxNode? root, int inputLength, bool successAtEof)
    {
        var absorberBytes = root is null ? 0 : CountAbsorberBytes(root);
        var absorberFraction = inputLength == 0 ? 0 : absorberBytes * 100 / inputLength;
        var recoveryNodes = root is null ? 0 : CostCalculator.CountRecoveryNodes(root);
        var coversInput = CoversInput(root, inputLength);
        var verdict = !successAtEof
            ? ParseVerdict.Failed
            : !coversInput
                ? ParseVerdict.Uncovered
                : absorberBytes * 2 > inputLength
                    ? ParseVerdict.FalseGreen
                    : absorberBytes == 0
                        ? ParseVerdict.Clean
                        : ParseVerdict.Recovered;
        return new ParseHealthReport(successAtEof, absorberBytes, absorberFraction, recoveryNodes, coversInput, verdict);
    }

    private static bool CoversInput(ISyntaxNode? root, int inputLength)
    {
        var spans = new List<(int StartPos, int EndPos)>();
        if (root is not null)
            CollectTerminalSpans(root, spans);
        if (spans.Count == 0)
            return inputLength == 0;
        spans.Sort((a, b) => a.StartPos.CompareTo(b.StartPos));
        if (spans[0].StartPos != 0)
            return false;
        for (var i = 1; i < spans.Count; i++)
            if (spans[i].StartPos != spans[i - 1].EndPos)
                return false;
        return spans[^1].EndPos == inputLength;
    }

    private static void CollectTerminalSpans(ISyntaxNode node, List<(int StartPos, int EndPos)> spans)
    {
        if (node is TerminalNode terminal)
            spans.Add((terminal.StartPos, terminal.EndPos));
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    CollectTerminalSpans(el, spans);
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    CollectTerminalSpans(el, spans);
                foreach (var d in list.Delimiters)
                    CollectTerminalSpans(d, spans);
                break;
            case SomeNode some:
                CollectTerminalSpans(some.Value, spans);
                break;
        }
    }

    private static int CountAbsorberBytes(ISyntaxNode root)
    {
        static int Sum(ISyntaxNode node)
        {
            var bytes = node is TerminalNode { IsAbsorber: true } terminal
                ? terminal.EndPos - terminal.StartPos
                : 0;
            switch (node)
            {
                case SeqNode seq:
                    foreach (var el in seq.RawElements)
                        bytes += Sum(el);
                    break;
                case ListNode list:
                    foreach (var el in list.RawElements)
                        bytes += Sum(el);
                    foreach (var d in list.Delimiters)
                        bytes += Sum(d);
                    break;
                case SomeNode some:
                    bytes += Sum(some.Value);
                    break;
            }
            return bytes;
        }
        return Sum(root);
    }
}
