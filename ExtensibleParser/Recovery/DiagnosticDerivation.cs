namespace ExtensibleParser.Recovery;

/// <summary>
/// A5-2: единый источник правды — recovery-диагностика выводится из финального дерева чистой
/// функцией (дерево → список), без чтения/мутации состояния парсера.
///
/// Дерево несёт ровно два вида recovery-узлов (оба — <see cref="TerminalNode"/> с
/// <see cref="Node.IsRecovery"/> == true, см. <c>SyntaxTree.cs</c>):
///   • нулевая вставка: IsRecovery, не <see cref="TerminalNode.IsAbsorber"/>, ширина 0
///     (вставленный missing token) → <see cref="RecoveryKind.Inserted"/>;
///   • абсорбер: IsAbsorber, ширина &gt; 0 (пропущенный регион [StartPos..EndPos))
///     → <see cref="RecoveryKind.Skipped"/>, диагностика несёт текст узла.
///
/// <see cref="RecoveryKind.Unrecovered"/> НЕ является узлом дерева: это нулевой маркер в точке
/// восстановления, добавляемый при принятии S6-дна (A4-2, Parser.Recovery.cs <c>AddUnrecoveredIfS6</c>)
/// и выводимый из РЕЗУЛЬТАТА, а не из дерева — поэтому в обходе дерева не выдаётся.
/// Реальное совпадение recovery-терминала (напр. ErrorOperator: IsRecovery, не абсорбер, ширина &gt; 0)
/// — не дыра и диагностики не порождает.
/// </summary>
public static class DiagnosticDerivation
{
    /// <summary>
    /// Выводит recovery-диагностику из дерева, отсортированную по позиции (детерминированно;
    /// тай-брейк по EndPos, затем по Kind). Чистая функция: одно дерево + вход → один список.
    /// </summary>
    /// <param name="root">Корень финального дерева (узел start-правила).</param>
    /// <param name="input">Исходный ввод — для текста абсорбера.</param>
    public static List<RecoveryDiagnostic> DeriveDiagnostics(ISyntaxNode root, string input)
    {
        var diagnostics = new List<RecoveryDiagnostic>();
        Walk(root, input, diagnostics);
        diagnostics.Sort(CompareByPosition);
        return diagnostics;
    }

    private static int CompareByPosition(RecoveryDiagnostic a, RecoveryDiagnostic b)
    {
        var byStart = a.StartPos.CompareTo(b.StartPos);
        if (byStart != 0)
            return byStart;
        var byEnd = a.EndPos.CompareTo(b.EndPos);
        if (byEnd != 0)
            return byEnd;
        return a.Kind.CompareTo(b.Kind);
    }

    // Обход полного дерева (RawElements — включая абсорберы, а не «чистых» Elements).
    private static void Walk(ISyntaxNode node, string input, List<RecoveryDiagnostic> diagnostics)
    {
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    Walk(el, input, diagnostics);
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    Walk(el, input, diagnostics);
                foreach (var d in list.Delimiters)
                    Walk(d, input, diagnostics);
                break;
            case SomeNode some:
                Walk(some.Value, input, diagnostics);
                break;
            case TerminalNode { IsRecovery: true } terminal:
                Emit(terminal, input, diagnostics);
                break;
        }
    }

    private static void Emit(TerminalNode terminal, string input, List<RecoveryDiagnostic> diagnostics)
    {
        if (terminal.IsAbsorber)
        {
            // Диагностика несёт текст узла (пропущенный регион) прямо в Message — единый источник правды.
            var text = input.Substring(terminal.StartPos, terminal.EndPos - terminal.StartPos);
            diagnostics.Add(new RecoveryDiagnostic(terminal.StartPos, terminal.EndPos, RecoveryKind.Skipped, text, null, null));
        }
        else if (terminal.EndPos == terminal.StartPos)
        {
            diagnostics.Add(new RecoveryDiagnostic(terminal.StartPos, terminal.EndPos, RecoveryKind.Inserted, $"inserted {terminal.Kind}", null, null));
        }
        // Иначе — реальное совпадение recovery-терминала: не дыра, диагностика не выдаётся.
    }
}
