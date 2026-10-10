namespace ExtensibleParser.Recovery;

/// <summary>
/// Вердикт о здоровье разбора: <see cref="Clean"/> — без восстановления;
/// <see cref="Recovered"/> — успех достигнут с восстановлением;
/// <see cref="FalseGreen"/> — Success@EOF, но более половины входа поглощено мусором;
/// <see cref="Failed"/> — парсер не дошёл до конца входа;
/// <see cref="Uncovered"/> — Success@EOF, но терминальные спаны дерева не покрывают вход
/// даже с учётом тривиума (нарушен инвариант I4: разбор заявляет успех, не покрывая вход).
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
/// и в процентах от входа, байты входа, покрытые объединением всех терминальных спанов,
/// число recovery-узлов, признак покрытия входа терминальными спанами дерева с учётом
/// тривиума (инвариант I4) и вердикт.
/// </summary>
public sealed record ParseHealthReport(
    bool SuccessAtEof,
    int AbsorberBytes,
    int AbsorberFraction,
    int UnionCoverage,
    int RecoveryNodes,
    bool CoversInput,
    ParseVerdict Verdict);

/// <summary>
/// Единый источник истины о «здоровье» разбора: по дереву и входу считает объём
/// абсорберов, recovery-узлы, покрытие входа и выносит вердикт. FalseGreen — это Success@EOF,
/// достигнутый поглощением более половины входа как мусора. Инвариант покрытия (I4) учитывает
/// тривиум: терминальные спаны дерева должны покрывать вход, причём пропуски между спанами
/// допускаются только тогда, когда целиком съедаются терминалом тривиума; наложения спанов
/// допускаются. Uncovered ловит Success@EOF при пустом/неполном дереве (memo-патч без реального разбора).
/// </summary>
public static class ParseHealth
{
    /// <summary>
    /// Анализирует дерево разбора: байты абсорберов, их долю от входа (в процентах),
    /// байты входа, покрытые объединением всех терминальных спанов, число recovery-узлов,
    /// покрытие входа терминальными спанами с учётом тривиума (инвариант I4)
    /// и вердикт в порядке Failed → Uncovered → FalseGreen → Clean → Recovered.
    /// </summary>
    public static ParseHealthReport Analyze(ISyntaxNode? root, string input, bool successAtEof, Terminal trivia)
    {
        var inputLength = input.Length;
        var absorberBytes = root is null ? 0 : CountAbsorberBytes(root);
        var absorberFraction = inputLength == 0 ? 0 : absorberBytes * 100 / inputLength;
        var recoveryNodes = root is null ? 0 : CostCalculator.CountRecoveryNodes(root);
        var spans = new List<(int StartPos, int EndPos)>();
        if (root is not null)
            CollectTerminalSpans(root, spans);
        spans.Sort((a, b) => a.StartPos.CompareTo(b.StartPos));
        var coversInput = CoversInput(input, spans, trivia);
        var unionCoverage = UnionCoverage(spans);
        var verdict = !successAtEof
            ? ParseVerdict.Failed
            : !coversInput
                ? ParseVerdict.Uncovered
                : absorberBytes * 2 > inputLength
                    ? ParseVerdict.FalseGreen
                    : absorberBytes == 0
                        ? ParseVerdict.Clean
                        : ParseVerdict.Recovered;
        return new ParseHealthReport(successAtEof, absorberBytes, absorberFraction, unionCoverage, recoveryNodes, coversInput, verdict);
    }

    /// <summary>
    /// Покрытие входа (инвариант I4, с учётом тривиума): спаны отсортированы по StartPos;
    /// пропуск перед первым спаном, между спанами и после последнего должен целиком
    /// съедаться терминалом тривиума; наложения спанов терпимы.
    /// </summary>
    private static bool CoversInput(string input, List<(int StartPos, int EndPos)> spans, Terminal trivia)
    {
        if (spans.Count == 0)
            return input.Length == 0;
        if (!IsTriviaGap(input, 0, spans[0].StartPos, trivia))
            return false;
        var coveredUntil = spans[0].EndPos;
        for (var i = 1; i < spans.Count; i++)
        {
            var next = spans[i];
            if (next.StartPos > coveredUntil && !IsTriviaGap(input, coveredUntil, next.StartPos, trivia))
                return false;
            coveredUntil = Math.Max(coveredUntil, next.EndPos);
        }
        return IsTriviaGap(input, coveredUntil, input.Length, trivia);
    }

    /// <summary>
    /// Поедается ли интервал [from, to) целиком терминалом тривиума: шаг за шагом,
    /// отказ при провале TryMatch (len &lt; 0) или при отсутствии прогресса (len == 0).
    /// </summary>
    private static bool IsTriviaGap(string input, int from, int to, Terminal trivia)
    {
        var pos = from;
        while (pos < to)
        {
            var len = trivia.TryMatch(input, pos);
            if (len <= 0)
                return false;
            pos += len;
        }
        return true;
    }

    /// <summary>
    /// Суммарный размер объединения всех спанов (сортировка по StartPos обязательна):
    /// перекрывающиеся и соприкасающиеся интервалы сливаются в один.
    /// </summary>
    private static int UnionCoverage(List<(int StartPos, int EndPos)> spans)
    {
        if (spans.Count == 0)
            return 0;
        var total = 0;
        var currentStart = spans[0].StartPos;
        var currentEnd = spans[0].EndPos;
        for (var i = 1; i < spans.Count; i++)
        {
            var (start, end) = spans[i];
            if (start <= currentEnd)
            {
                if (end > currentEnd)
                    currentEnd = end;
            }
            else
            {
                total += currentEnd - currentStart;
                currentStart = start;
                currentEnd = end;
            }
        }
        return total + currentEnd - currentStart;
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
