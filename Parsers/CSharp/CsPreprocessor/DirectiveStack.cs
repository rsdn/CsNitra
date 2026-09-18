namespace CsPreprocessor;

// The preprocessor's running state (D4, mirrors Roslyn DirectiveStack): the active/inactive
// status of the current region plus the symbol table from #define/#undef + command-line symbols.
// Pure logic, tree-free, driven by a sequence of operations.
public sealed class DirectiveStack
{
    private readonly HashSet<string> _commandLineSymbols;
    private readonly Stack<IfFrame> _ifStack = new();
    private readonly List<DefineOp> _defineOps = new();
    private bool _isActive = true;

    public DirectiveStack(IReadOnlyCollection<string> commandLineSymbols)
    {
        _commandLineSymbols = new HashSet<string>(commandLineSymbols, StringComparer.Ordinal);
    }

    public bool IsActive => _isActive;

    public bool HasUnfinishedIf => _ifStack.Count > 0;

    public bool CurrentFrameHasElse => _ifStack.Count > 0 && _ifStack.Peek().HasElse;

    public bool If(bool condition)
    {
        var branchTaken = _isActive && condition;
        _ifStack.Push(new IfFrame(_isActive, branchTaken));
        _isActive = branchTaken;
        return branchTaken;
    }

    public bool Elif(bool condition)
    {
        if (_ifStack.Count == 0)
            return false;

        var frame = _ifStack.Peek();
        var branchTaken = frame.EndIsActive && condition && !frame.PreviousBranchTaken;
        frame.PreviousBranchTaken = frame.PreviousBranchTaken || branchTaken;
        _isActive = branchTaken;
        return branchTaken;
    }

    public bool Else()
    {
        if (_ifStack.Count == 0)
            return false;

        var frame = _ifStack.Peek();
        var branchTaken = frame.EndIsActive && !frame.PreviousBranchTaken;
        frame.PreviousBranchTaken = frame.PreviousBranchTaken || branchTaken;
        frame.HasElse = true;
        _isActive = branchTaken;
        return branchTaken;
    }

    public void EndIf()
    {
        if (_ifStack.Count == 0)
            return;

        var frame = _ifStack.Pop();
        _isActive = frame.EndIsActive;
    }

    public void Define(string symbol)
    {
        if (_isActive)
            _defineOps.Add(new DefineOp(symbol, IsDefined: true));
    }

    public void Undef(string symbol)
    {
        if (_isActive)
            _defineOps.Add(new DefineOp(symbol, IsDefined: false));
    }

    public bool IsDefined(string symbol)
    {
        for (var i = _defineOps.Count - 1; i >= 0; i--)
        {
            var op = _defineOps[i];
            if (op.Symbol == symbol)
                return op.IsDefined;
        }

        return _commandLineSymbols.Contains(symbol);
    }

    private sealed record IfFrame
    {
        public IfFrame(bool endIsActive, bool previousBranchTaken)
        {
            EndIsActive = endIsActive;
            PreviousBranchTaken = previousBranchTaken;
        }

        public bool EndIsActive { get; init; }

        public bool PreviousBranchTaken { get; set; }

        public bool HasElse { get; set; }
    }

    private sealed record DefineOp(string Symbol, bool IsDefined);
}
