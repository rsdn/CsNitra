#nullable enable

using System.Diagnostics;
using Diagnostics;

namespace ExtensibleParser;

using ExtensibleParser.Recovery;

// Recovery-подсистема: состояние, хуки и цикл восстановления.
public partial class Parser
{
    private int _recoveryPoint = -1;
    private int _quietZoneEnd = -1;
    private FailureSnapshot? _lastSnapshot;
    private bool _suppressSideEffects;
    public FailureSnapshot? LastSnapshot => _lastSnapshot;
    private Result? _lastPartial;
    public Result? LastPartial => _lastPartial;
    // A5-2: internal cache of the accumulated diagnostics (still written by AddUnrecoveredIfS6, the
    // per-accepted-candidate AddRange, and the soft-separator emission/dedup in ParseSeparatedList).
    // It is NO LONGER the public list — that is _finalRecoveryDiagnostics (6.1.2c, derived below).
    private readonly List<RecoveryDiagnostic> _recoveryDiagnostics = [];
    // A5-2 (6.1.2c): the PUBLIC diagnostic list, DERIVED from the final tree at finalization:
    // DeriveRecoveryDiagnostics(finalTree) (the node-attached diagnostics — the exact accumulated
    // instances — in tree position order) plus the Unrecovered marker(s) from the _recoveryDiagnostics
    // cache (Unrecovered is not a tree node — A4-2 — so it is appended, not tree-derived). Rebuilt once
    // per Parse in FinishRecovery (the single point where the final tree + result are available).
    private List<RecoveryDiagnostic> _finalRecoveryDiagnostics = [];
    public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics => _finalRecoveryDiagnostics;

    // A5-2 (6.1.2a): node-attached recovery diagnostics via a side-table keyed by node reference
    // identity. The immutable Node records carry structure only; the RecoveryDiagnostic metadata
    // (kind, Terminal, RuleName, message) lives here, keyed by the node's reference identity.
    // ConditionalWeakTable<TKey,TValue> is UNAVAILABLE in netstandard2.0 (verified: a clean
    // netstandard2.0 probe fails with CS0246), so the prescribed fallback is used:
    // Dictionary<WeakReference<ISyntaxNode>, RecoveryDiagnostic[]>. The weak key is future-proofing
    // for Wave 8 (subtree reuse); harmless for per-parse use (the table is re-created each Parse).
    // The key compares the WeakReference's TARGET by reference and identity-hashes it — the same
    // reference-identity pattern as ReferenceComparer (5a.5.1), which is Rule-typed and so cannot be
    // reused directly for this key type. Re-initialized fresh per Parse (see Parser.Parse).
    private Dictionary<WeakReference<ISyntaxNode>, RecoveryDiagnostic[]> _diagSideTable = new(WeakRefNodeKeyComparer.Instance);

    // A5-2 (6.1.2a/6.1.2b): registration hook — the recovery strategies' diagnostics are attached
    // to their final-tree nodes by the session-end position+shape match (FinishRecovery, 6.1.2b).
    // Accumulates: multiple diagnostics on the same node all survive.
    public void AttachDiagnostic(ISyntaxNode node, RecoveryDiagnostic diag)
    {
        var key = new WeakReference<ISyntaxNode>(node);
        if (!_diagSideTable.TryGetValue(key, out var existing))
        {
            _diagSideTable[key] = [diag];
            return;
        }
        _diagSideTable[key] = [.. existing, diag];
    }

    // A5-2 (6.1.2a): side-table derivation — walk the final tree, collect each node's attached
    // diagnostics by reference identity, return them sorted by (StartPos, EndPos). This is the
    // reference-identity lookup (returns the exact accumulated instances), distinct from the pure
    // structure-based DiagnosticDerivation.DeriveDiagnostics (6.1.1). It is the source of the
    // node-attached part of the public list (6.1.2c); the Unrecovered marker is appended separately.
    public IReadOnlyList<RecoveryDiagnostic> DeriveRecoveryDiagnostics(ISyntaxNode root)
    {
        var result = new List<RecoveryDiagnostic>();
        CollectAttached(root, result);
        result.Sort((a, b) =>
            a.StartPos == b.StartPos ? a.EndPos.CompareTo(b.EndPos) : a.StartPos.CompareTo(b.StartPos));
        return result;
    }

    // Обход полного дерева (RawElements — включая абсорберы, а не «чистых» Elements); на каждом
    // узле — lookup в side-table по ссылке на узел.
    private void CollectAttached(ISyntaxNode node, List<RecoveryDiagnostic> result)
    {
        if (_diagSideTable.TryGetValue(new WeakReference<ISyntaxNode>(node), out var diags))
            result.AddRange(diags);
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    CollectAttached(el, result);
                break;
            case ListNode ln:
                foreach (var el in ln.RawElements)
                    CollectAttached(el, result);
                foreach (var d in ln.Delimiters)
                    CollectAttached(d, result);
                break;
            case SomeNode some:
                CollectAttached(some.Value, result);
                break;
            // TerminalNode / NoneNode / PredicateNode — листья, дочерних узлов нет.
        }
    }

    // A5-2 (6.1.2a): reference-identity comparer for the side-table's WeakReference<ISyntaxNode>
    // keys — compares the target node by reference and identity-hashes it (the same pattern as
    // ReferenceComparer, 5a.5.1; that one is Rule-typed, so it is not reusable for this key type).
    private sealed class WeakRefNodeKeyComparer : IEqualityComparer<WeakReference<ISyntaxNode>>
    {
        public static readonly WeakRefNodeKeyComparer Instance = new();

        public bool Equals(WeakReference<ISyntaxNode>? x, WeakReference<ISyntaxNode>? y)
        {
            GetTarget(x, out var xt);
            GetTarget(y, out var yt);
            return ReferenceEquals(xt, yt);
        }

        public int GetHashCode(WeakReference<ISyntaxNode> obj) =>
            obj.TryGetTarget(out var target) ? _IdentityHash(target!) : 0;

        // netstandard2.0's WeakReference<T> exposes TryGetTarget (no Target property).
        private static void GetTarget(WeakReference<ISyntaxNode>? wr, out ISyntaxNode? target)
        {
            if (wr is null || !wr.TryGetTarget(out var t))
            {
                target = null;
                return;
            }
            target = t;
        }

        private static readonly Func<object, int> _IdentityHash = CreateIdentityHash();

        // Identity hash (BCL RuntimeHelpers.GetHashCode) via a delegate: the local
        // System.Runtime.CompilerServices.RuntimeHelpers (Shared/NetStandard2_0Support.cs) shadows the
        // BCL type by name, so the BCL method is resolved by reflection once (same as ReferenceComparer).
        private static Func<object, int> CreateIdentityHash()
        {
            var method = typeof(object)
                .Assembly
                .GetType("System.Runtime.CompilerServices.RuntimeHelpers")
                ?.GetMethod("GetHashCode", [typeof(object)]);
            return method is null
                ? static o => o.GetHashCode()
                : (Func<object, int>)Delegate.CreateDelegate(typeof(Func<object, int>), method)!;
        }
    }

    // Хук для тестов (паттерн LastSnapshot/LastPartial): число ленивых вызовов RecoveryEngine.Generate в последнем Parse.
    public int EngineGenerateCalls { get; private set; }

    // Хук для тестов (2.4, бенчмарк): число проходов recovery-цикла (итераций) в последнем Parse.
    // Сбрасывается в начале Parse, инкрементируется на каждой итерации цикла восстановления.
    public int RecoveryPasses { get; private set; }

    // Хук для тестов (2.1/B1, точная гигиена): число удалений memo, выполненных HygieneCore за последний Parse.
    // Сбрасывается в начале Parse; ограниченно (не пропорционально размеру файла) при точной гигиене.
    public int HygieneRemovals { get; private set; }

    // Хук для тестов (5a.2.2, дешёвые S2-сканы): число позиций, реально проверенных S2-сканом за последний Recover.
    // Инкремент — в RecoveryEngine (5a.2.2); сброс — в начале Recover.
    public int S2ScanPositions { get; private set; }

    // Хук для тестов (5a.2.3, дешёвые S3-сканы): число позиций, реально проверенных S3-сканом за последний Recover.
    // Инкремент — в RecoveryEngine (5a.2.3); сброс — в начале Recover.
    public int S3ScanPositions { get; private set; }

    // Note-методы (5a.2.1a): публичные точки инкремента для движка (у счётчиков private set).
    public void NoteS2ScanPosition() => S2ScanPositions++;
    public void NoteS3ScanPosition() => S3ScanPositions++;

    // B2: кэш спекулятивных парсов (обёртка SpeculativeCache) + D2-счётчики хитов/промахов (read-only,
    // читаются из обёртки). Время жизни кэша — один Recover; сброс (кэш + счётчики) — в начале Recover.
    public SpeculativeCache SpecCache => _specCache;
    public int SpecCacheHits => _specCache.Hits;
    public int SpecCacheMisses => _specCache.Misses;

    // D2-full (6.2.1): observation-only metrics for the last recovery session (Parse) — accept/rollback
    // per strategy S0..S6 + specCache hits/misses (read through the shared SpeculativeCache).
    public RecoveryMetrics Metrics => _metrics ??= new(_specCache);

    // A4-6 (6.3.1): observation-only GRAMMAR-QUALITY diagnostics — feedback for the grammar author
    // about their recovery annotations (anchor usage, recovery points per rule, strict-region firing,
    // S2-anchor redundancy). SEPARATE from Metrics (per-session) and RecoveryDiagnostics (per-input):
    // this ACCUMULATES across parses for the lifetime of the Parser (the signals are corpus-level).
    public GrammarDiagnostics GrammarDiagnostics => _grammarDiagnostics ??= new();

    // A3: единый источник recovery-лимитов. Convenience-свойства ниже читают/пишут Profile,
    // сохраняя существующие тесты, которые ставят их напрямую.
    public RecoveryProfile Profile { get; set; } = RecoveryProfile.Ide;

    // B4: current degradation level (0 = full). Raised when the recovery session
    // exceeds Profile.TimeBudget. Public + settable so tests can drive the ladder
    // deterministically (no wall-clock in tests).
    public int DegradationLevel { get; set; }

    // B4.4: test hook — starting degradation level for the recovery session (default 0).
    // Production leaves it at 0 (each parse starts fresh). Tests set it to drive the ladder
    // deterministically (e.g. 3 = hard limit) without relying on the wall-clock TimeBudget,
    // since the Recover-start reset would otherwise clobber a pre-set DegradationLevel.
    public int ForcedDegradationLevel { get; set; }

    private Stopwatch _recoveryStopwatch = new();

    // B4: level-aware effective properties, derived from DegradationLevel via the Degradation table.
    public RecoveryStrategy EffectiveStrategyMask =>
        Profile.StrategyMask & Degradation.Get(DegradationLevel).Mask;

    public int EffectiveMaxSkip =>
        (int)(Profile.MaxSkip * Degradation.Get(DegradationLevel).MaxSkipMultiplier);

    public bool SpeculationEnabled => Degradation.Get(DegradationLevel).Speculation;

    public bool ForceS6 => Degradation.Get(DegradationLevel).ForceS6;

    // Лимиты цикла восстановления: предельное число итераций (Profile.MaxIterations).
    public int MaxRecoveryIterations
    {
        get => Profile.MaxIterations;
        set => Profile = Profile with { MaxIterations = value };
    }

    // A2: бюджеты по тирам (не по кандидатам). Каждый тир — свой под-бюджет: число кандидатов тира,
    // допустимое на точке восстановления. «Попыткой» считается кандидат тира; S0 (ранг 0) и S6 (ранг 6)
    // вне под-бюджетов (S6 — гарантированное дно, 1.3.2). Под-бюджеты раздельны, поэтому S1 (много
    // вставок) не выедает общий бюджет и не глушит S2/S3/S6. Значения — Profile (A3 RecoveryProfile).
    public int S1TierBudget
    {
        get => Profile.S1TierBudget;
        set => Profile = Profile with { S1TierBudget = value };
    }
    public int S2TierBudget
    {
        get => Profile.S2TierBudget;
        set => Profile = Profile with { S2TierBudget = value };
    }
    public int S3S6TierBudget
    {
        get => Profile.S3S6TierBudget;
        set => Profile = Profile with { S3S6TierBudget = value };
    }

    // Legacy (до A2): пер-позиционный бюджет кандидатов. Сохранён для совместимости (тесты его ставят),
    // но более не ограничивает выбор кандидатов — вместо него под-бюджеты тиров (A2).
    public int MaxRecoveryAttemptsPerPosition { get; set; } = 3;
    private readonly Dictionary<int, TierBudget> _attempts = new();

    // B2: кэш спекулятивных парсов на scratch-копии, время жизни = один Recover (сброс в начале Recover).
    private readonly SpeculativeCache _specCache = new();

    // D2-full (6.2.1): метрики последней recovery-сессии (accept/rollback per S0..S6 + specCache).
    // Наблюдательные; время жизни = один Recover (сброс в начале Recover, вместе с _specCache).
    // Не readonly / null до первого создания: field-инициализатор не может ссылаться на _specCache
    // (CS0236), поэтому создаётся лениво в свойстве Metrics и в начале Recover.
    private RecoveryMetrics? _metrics;

    // A4-6 (6.3.1): grammar-quality diagnostics, lazy-created in the GrammarDiagnostics property.
    // Не сбрасывается в начале Recover (накопление на протяжении жизни Parser — корпусные сигналы).
    private GrammarDiagnostics? _grammarDiagnostics;

    // Состояние, перенесённое из ядра: используется только recovery-подсистемой
    // (ядро обращается к нему исключительно через partial-хуки в Parser.cs).
    private FollowSetCalculator? _followCalculator;
    private readonly List<StackFrame> _stackFrames = [];
    private readonly Dictionary<(int Pos, Terminal Terminal), Injection> _injections = new(TerminalComparer.KeyComparer);
    private int _parseDepth;
    // Default for parsers that never go through Parse (e.g. the recovery engine's speculative scratch
    // parser, which calls ParseRule directly): the same absolute cap SetMaxParseDepth applies
    // (stack-safety constant). The old default (4096) was ~4x the real stack budget (~600 frames) —
    // recalibrated in 7.1.1/R3.
    private int _maxParseDepth = RecoveryProfile.MaxParseDepthCap;

    // Хук для тестов (7.1.1/R3): максимальная глубина _parseDepth, достигнутая в последнем Parse
    // (0 — ни одного кадра не начато). Сбрасывается в SetMaxParseDepth; используется для калибровки
    // depth-guard и (7.1.2) для диагностики сработавшего guard'а.
    private int _maxParseDepthReached;
    public int MaxParseDepthReached => _maxParseDepthReached;

    // 7.1.2/R3: сработал ли depth-guard за последний Parse (любой кадр отклонён GuardFired).
    // Сбрасывается в SetMaxParseDepth (как _maxParseDepthReached). Позиция ПЕРВОГО срабатывания —
    // самая глубокая достигнутая точка (начальный спуск бьётся в потолок раньше всех повторных
    // спусков/ре-парсов, т.к. GuardFired сжимает лимит после каждого срабатывания) — точка, где
    // guard «оборвал» ветку; на неё позиционируется диагностика InsufficientStack (дно-контракт A5-4).
    private bool _guardFired;
    private int _guardFiredPos;

    // Хук для тестов/engine: инжекции в Фазе 0 никто не порождает, слой активен с Фазы 1.
    public void AddInjection(Terminal terminal, int pos, Injection injection) => _injections[(pos, terminal)] = injection;

    // Хуки engine (1.2): применение/откат инъекций с сохранением старого значения.
    public IReadOnlyDictionary<(int Pos, Terminal Terminal), Injection> Injections => _injections;
    public void ApplyInjection(Terminal terminal, int pos, Injection injection)
    {
        var key = (pos, terminal);
        OnInjectionApplied(key);
        _injections[key] = injection;
    }

    // oldValue == null — ключа не было (удалить), иначе — вернуть старое значение.
    public void RollbackInjection(Terminal terminal, int pos, Injection? oldValue)
    {
        if (oldValue is { } old)
            _injections[(pos, terminal)] = old;
        else
            _injections.Remove((pos, terminal));
    }

    // Хуки engine (1.2): доступ к memo для патчей/инспекции.
    public IReadOnlyDictionary<(int pos, string rule, int precedence), Result> Memo => _memo;
    public void SetMemo(string rule, int pos, int precedence, Result value)
    {
        var key = (pos, rule, precedence);
        OnMemoWritten(key);
        _memo[key] = value;
    }

    public void RemoveMemo(string rule, int pos, int precedence)
    {
        var key = (pos, rule, precedence);
        OnMemoRemoved(key);
        _memo.Remove(key);
    }

    // Патч для всех прецедентов (e, rule, prec'), присутствующих в memo (TDOPP, §3.4 S3).
    // Порядок Hygiene→Apply (A1/S6): Hygiene может удалить Failure на (pos, rule) до Apply —
    // тогда ключей нет и без этого патч no-op'ит, re-парс не видит абсорбер (регресс S2/S3 +
    // потеря memo, ломающего рекурсию NotPredicate → stack overflow). Создаём прецедент 0.
    public void PatchMemo(string rule, int pos, Result value)
    {
        var keys = _memo.Keys.Where(k => k.pos == pos && k.rule == rule).ToList();
        if (keys.Count == 0)
            keys.Add((pos, rule, 0));
        foreach (var key in keys)
        {
            OnMemoWritten(key);
            _memo[key] = value;
        }
    }

    public FollowSetCalculator? FollowCalculator => _followCalculator;
    public Terminal[] GetTerminators(IReadOnlyList<StackFrame> stack) =>
        _followCalculator?.GetTerminators(stack) ?? [EofTerminal.Instance];
    public IReadOnlyList<StackFrame> CurrentStackFrames => _stackFrames;

    // Одноразовый parse правила без recovery-цикла (спекулятивная валидация engine'а, §3.4 S2).
    public Result ParseRuleOnce(string ruleName, int minPrecedence, int startPos, string input) =>
        ParseRule(ruleName, minPrecedence, startPos, input);

    // Логи патчей текущего кандидата (memo и инъекции): заполняются хуками, пока активны, — основа отката.
    private List<MemoPatch>? _memoPatchLog;
    private List<InjectionPatch>? _injectionPatchLog;

    // ============ Реализации хуков (декларации — в Parser.cs) ============

    private partial void OnMismatch(Terminal terminal, int pos) => CaptureSnapshot(pos, terminal);
    private partial void OnMemoWritten((int pos, string rule, int precedence) key)
    {
        IndexMemoAdd(key);
        RecordMemo(key);
    }

    private partial void OnMemoRemoved((int pos, string rule, int precedence) key)
    {
        IndexMemoRemove(key);
        RecordMemoRemove(key);
    }
    private partial void OnInjectionApplied((int Pos, Terminal Terminal) key) => RecordInjection(key);
    private partial void OnPartialCaptured(Result partialResult) => _lastPartial = partialResult;
    private partial bool IsRecoveryPosition(int pos) => pos == _recoveryPoint;
    private partial bool InQuietZone(int pos) => _quietZoneEnd >= 0 && pos <= _quietZoneEnd;
    private partial bool SuppressSideEffects => _suppressSideEffects;
    private partial void ResetRecoveryPoint() => _recoveryPoint = -1;

    // ============ Методы, перенесённые из ядра (recovery-only) ============

    // Точка восстановления E (§2): Failure → ErrorPos; Partial → max(NewPos, ErrorPos);
    // Success до EOF → max(NewPos, ErrorPos): NewPos — хвостовой мусор, ErrorPos — самая дальняя точка падения.
    private int RecoveryPointOf(Result result, string input)
    {
        if (result.TryGetSuccess(out _, out var end))
            return end < input.Length ? Math.Max(end, ErrorPos) : input.Length;
        if (result.TryGetPartial(out _, out var partialEnd))
            return Math.Max(partialEnd, ErrorPos);
        return ErrorPos;
    }

    // Инъекция: Length == 0 — пустой узел-вставка, Length > 0 — абсорбер [pos..pos+Length).
    private Result CreateInjectedResult(Injection injection, int pos)
    {
        var endPos = pos + injection.Length;
        var node = new TerminalNode(injection.NodeKind, pos, endPos, injection.Length, IsRecovery: true, IsAbsorber: injection.IsSkip && injection.Length > 0);
        return Result.Success(node, endPos, endPos);
    }

    // §3.9/2.3: OftenMissed — сахар над TryInsert: кадр несёт RecoveryOptions(TryInsert: [элемент]),
    // чтобы engine (S1, ранг 0) генерировал кандидата вставки терминала в точке восстановления.
    private static RecoveryOptions? OftenMissedOptions(Rule rule) =>
        rule is OftenMissed { Element: Terminal terminal }
            ? new RecoveryOptions { TryInsert = [terminal] }
            : null;

    // ============ Реализации хуков (продолжение) ============

    private partial void InitFollowCalculator(Dictionary<string, Rule[]> rules) => _followCalculator = new FollowSetCalculator(rules);
    private partial void SetMaxParseDepth(int inputLength)
    {
        _parseDepth = 0;
        _maxParseDepthReached = 0;
        _guardFired = false;
        _guardFiredPos = 0;
        // 7.1.1/R3: the per-char limit is unbounded, but the thread stack budget is a FIXED number of
        // frames — the absolute cap keeps the guard firing before a stack overflow for long inputs
        // (empirics: `Expr := "(" Expr ")" | Digits`, 350 nesting levels = crash under the old limit).
        _maxParseDepth = Math.Min(Profile.MaxParseDepthBase + inputLength * Profile.MaxParseDepthPerChar,
            RecoveryProfile.MaxParseDepthCap);
    }
    private partial void ClearInjections() => _injections.Clear();

    // Roslyn StackGuard constant (Microsoft.CodeAnalysis.StackGuard.MaxUncheckedRecursionDepth):
    // frames at or below this depth cannot exhaust the thread stack — skip the per-frame check there.
    private const int MaxUncheckedRecursionDepth = 20;

    private static readonly Action _ensureSufficientExecutionStack = CreateEnsureSufficientExecutionStack();

    private static void EnsureSufficientExecutionStack() => _ensureSufficientExecutionStack();

    // BCL RuntimeHelpers.EnsureSufficientExecutionStack() via a delegate: the local
    // System.Runtime.CompilerServices.RuntimeHelpers (Shared/NetStandard2_0Support.cs) shadows the
    // BCL type by name, so the BCL method is resolved by reflection once (same as ReferenceComparer,
    // Parser.cs). Unresolvable → no-op (the depth cap still protects).
    private static Action CreateEnsureSufficientExecutionStack()
    {
        var method = typeof(object)
            .Assembly
            .GetType("System.Runtime.CompilerServices.RuntimeHelpers")
            ?.GetMethod("EnsureSufficientExecutionStack", Type.EmptyTypes);
        return method is null
            ? static () => { }
            : (Action)Delegate.CreateDelegate(typeof(Action), method)!;
    }

    private partial bool BeginParseFrame(int startPos)
    {
        // 7.1.1/R3: the guard checks BEFORE incrementing, so a rejected frame leaves no dangling
        // increment (ParseAlternative returns Failure on true without running EndParseFrame's
        // finally). The old increment-then-check leaked +1 per rejected frame — invisible under the
        // old unbounded limit, but under the calibrated cap the first firing inflated the counter
        // and made every retried descent (memo re-descent, recovery re-parse) fire the guard at a
        // shallower true depth than the calibration allows.
        if (_parseDepth >= _maxParseDepth)
            return GuardFired(startPos);
        // 7.1.1/R3: preventive remaining-stack check at the hottest recursion point (every rule
        // dispatch goes through ParseAlternative → BeginParseFrame). Roslyn StackGuard pattern:
        // shallow frames skip the check (MaxUncheckedRecursionDepth). The BCL method throws a
        // CATCHABLE InsufficientExecutionStackException when the thread has <60KB of stack left —
        // unlike a real StackOverflowException — so catching it = the guard fired (Failure branch).
        // Second line of defense under the depth cap: it is stack-size-agnostic (protects smaller
        // stacks where the calibrated cap alone would be too high).
        if (_parseDepth > MaxUncheckedRecursionDepth)
            try
            {
                EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                return GuardFired(startPos);
            }
        _parseDepth++;
        if (_parseDepth > _maxParseDepthReached)
            _maxParseDepthReached = _parseDepth;
        return false;
    }

    // 7.1.1/R3 fix (recovery hang): every firing shrinks the effective limit by one. With the
    // check-before-increment counter a rejected frame leaves no trace, so without the shrinkage
    // every backtrack alternative / memo re-descent / recovery re-parse re-pays the full descent
    // to the cap, and a recovery session (hundreds of re-parses, each firing the guard hundreds
    // of times) explodes. The shrinkage bounds total accepted frames per parse session to
    // O(cap²) — exactly the (accidental) total-work bound of the pre-7.1.1 increment-then-check
    // leak (leak +1 per firing ⇒ descent k accepts up to depth cap-k-1, same as here), which the
    // check-before-increment change removed. After enough firings the limit reaches 0 and every
    // frame is rejected at frame 1, so the recovery loop's fail-safe (e <= ePrev) ends the
    // session fast. The counter stays semantically clean (depth), and MaxParseDepthReached still
    // records the first firing depth exactly (R3 calibration assertion).
    private bool GuardFired(int firedAtPos)
    {
        // 7.1.2/R3: record the FIRST firing (deepest reached position) for the InsufficientStack
        // diagnostic — see the _guardFired/_guardFiredPos field comment for why the first firing is
        // the farthest point (GuardFired shrinks the limit, so later firings are shallower).
        if (!_guardFired)
        {
            _guardFired = true;
            _guardFiredPos = firedAtPos;
        }
        if (_maxParseDepth > 0)
            _maxParseDepth--;
        return true;
    }
    private partial void EndParseFrame() => _parseDepth--;

    private partial void PushRuleFrame(string ruleName, int minPrecedence, FrameLocation location, RecoveryOptions? options) =>
        _stackFrames.Add(new StackFrame(ruleName, minPrecedence, location, null, options));
    private partial void PopFrame() => _stackFrames.RemoveAt(_stackFrames.Count - 1);
    private partial Result WithFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, RecoveryOptions? options, Func<Result> action)
    {
        PushFrame(location, expected, fallbackRuleName, options);
        try
        {
            return action();
        }
        finally
        {
            PopFrame();
        }
    }

    private void PushFrame(FrameLocation location, Terminal[]? expected, string fallbackRuleName, RecoveryOptions? options)
    {
        var ruleName = _stackFrames.Count > 0 ? _stackFrames[^1].RuleName : fallbackRuleName;
        var precedence = _stackFrames.Count > 0 ? _stackFrames[^1].Precedence : 0;
        _stackFrames.Add(new StackFrame(ruleName, precedence, location, expected, options));
    }

    private partial Terminal[] ExpectedFor(Rule element) => FirstSets.Get(element, _followCalculator);
    private partial RecoveryOptions? OptionsFor(Rule rule) => rule is RecoveryRule rr ? rr.Options : OftenMissedOptions(rule);
    private partial RecoveryOptions? OftenMissedOptionsFor(Rule rule) => rule is OftenMissed { Element: Terminal terminal } ? new RecoveryOptions { TryInsert = [terminal] } : null;
    private partial Rule[] PrefixesFor(string ruleName, bool isRecoveryPos)
    {
        var tdoppRule = TdoppRules[ruleName];
        return isRecoveryPos ? tdoppRule.RecoveryPrefix : tdoppRule.Prefix;
    }
    private partial RuleWithPrecedence[] PostfixesFor(string ruleName, bool isRecoveryPos)
    {
        var tdoppRule = TdoppRules[ruleName];
        return isRecoveryPos ? tdoppRule.RecoveryPostfix : tdoppRule.Postfix;
    }
    private partial Result? InjectionAt(int pos, Terminal terminal) =>
        terminal.Injectable && _injections.TryGetValue((pos, terminal), out var injection) ? CreateInjectedResult(injection, pos) : null;
    private partial bool AcceptEpsilonMatch(Rule prefix) =>
        prefix is RecoveryRule rc && (rc.Options is null || rc.Options.Recoverable);
    private partial Result? ParseRecoveryRuleType(Rule rule, int startPos, string input) =>
        rule is RecoveryRule r ? ParseAlternative(r.Inner, startPos, input) : null;
    private partial Result? InsertOftenMissed(OftenMissed oftenMissed, int startPos, Result failedResult) =>
        IsRecoveryPosition(startPos)
            ? Result.Success(new TerminalNode(oftenMissed.Kind, startPos, startPos, ContentLength: 0, IsRecovery: true), startPos, failedResult.MaxFailPos)
            : null;
    private partial bool IsRecoveryTerminal(Terminal terminal) => terminal is RecoveryTerminal;
    private partial bool IsRecoveryRule(Rule alt) => alt.GetSubRules<RecoveryTerminal>().Any();
    private partial (Rule[] Prefix, RuleWithPrecedence[] Postfix) BuildRecoveryTdopp(string ruleName, Rule[] alternatives)
    {
        var recoveryPrefix = new List<Rule>();
        var recoveryPostfix = new List<RuleWithPrecedence>();
        foreach (var alt in alternatives)
        {
            if (alt is Seq { Elements: [Ref rule, .. var rest] } && rule.RuleName == ruleName)
            {
                var reqRef = rest.OfType<ReqRef>().FirstOrDefault();
                if (reqRef is { })
                    recoveryPostfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), reqRef.Precedence, reqRef.Right));
                else
                {
                    var precedence = rule is ReqRef x ? x.Precedence : 0;
                    recoveryPostfix.Add(new RuleWithPrecedence(Kind: alt.Kind, new Seq(rest, alt.Kind), precedence, Right: false));
                }
            }
            else
                recoveryPrefix.Add(alt);
        }
        return (recoveryPrefix.ToArray(), recoveryPostfix.ToArray());
    }

    // Финализация Parse (recovery): стек правил, таблица мемоизации, точка восстановления e, ErrorInfo.
    private partial void FinalizeResult(Result result, string input)
    {
        Log($"--- RULE STACK TRACE ---", LogImportance.High);
        foreach (var frame in _stackFrames)
            Log($"  Rule: {frame.RuleName}, Prec: {frame.Precedence}, Loc: {frame.Location}, Expected: [{string.Join(", ", (frame.Expected ?? Array.Empty<Terminal>()).Select(t => t.Kind))}]", LogImportance.High);
        Log($"------------------------", LogImportance.High);

        var debugInfos = MemoizationVisualazer(input);
        Log($"Parse failed. Memoization table:");
        foreach (var info in debugInfos)
            Log($"    {info.Info}");
        Log($"and of memoization table.");

        var e = RecoveryPointOf(result, input);

        // Финальные состояния §3.6: Success@EOF / Partial@EOF — восстановлено (ErrorInfo = null,
        // дыры описаны RecoveryDiagnostics); иначе — невосстановлено: FatalError в последней точке e.
        var recovered = (result.TryGetSuccess(out _, out var successEnd) && successEnd == input.Length)
            || (result.TryGetPartial(out _, out var partialEnd) && partialEnd == input.Length);
        ErrorInfo = recovered ? null : new FatalError(input, e, Location: input.PositionToLineCol(e), _expected.ToArray());
    }

    // Один parse (без fail-fast): setup recovery-состояния + цикл восстановления (S0 + ленивый Generate).
    private partial Result Recover(string input, string startRule, int currentStartPos)
    {
        _recoveryDiagnostics.Clear();
        _attempts.Clear();
        EngineGenerateCalls = 0;
        RecoveryPasses = 0;
        HygieneRemovals = 0;
        S2ScanPositions = 0;
        S3ScanPositions = 0;
        _specCache.Reset();
        var metrics = _metrics ??= new(_specCache);
        metrics.Reset();
        _recoveryPoint = -1;
        _quietZoneEnd = -1;
        _lastSnapshot = null;
        _lastPartial = null;
        _suppressSideEffects = false;

        // B4: reset the degradation mechanism for this recovery session.
        // B4.4: start at ForcedDegradationLevel (default 0) so tests can drive the ladder deterministically.
        DegradationLevel = ForcedDegradationLevel;
        _recoveryStopwatch.Restart();

        var ePrev = -1;
        var e = -1;
        var result = default(Result);

        for (var iter = 0; ; iter++)
        {
            RecoveryPasses++;

            // B4: if the recovery session exceeds the wall-clock budget, degrade (coarser).
            // Guard `TimeBudget > TimeSpan.Zero`: Timeout.InfiniteTimeSpan is -1ms, so without the
            // guard `Elapsed > TimeBudget` is always true and the Test profile would degrade instantly.
            if (Profile.TimeBudget > TimeSpan.Zero
                && DegradationLevel < Degradation.Levels.Length - 1
                && _recoveryStopwatch.Elapsed > Profile.TimeBudget)
                DegradationLevel++;

            Log($"Starting at {currentStartPos} iter={iter} parse for rule '{startRule}' _recoveryPoint={_recoveryPoint}");
            var passSw = Stopwatch.StartNew(); // D2-full (6.2.2): time-by-phase, observation-only
            result = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
            if (iter == 0)
                metrics.NoteMainParse(passSw.Elapsed); // (a) main parse — the initial parse before recovery
            else
                metrics.NoteReparse(passSw.Elapsed); // (c) iterative re-parse after the previous candidate was accepted

            if (result.TryGetSuccess(out _, out var end) && end == input.Length)
                return FinishRecovery(result); // чистый успех

            // A4-5.2: Compiler profile — bail on first failure, no recovery attempts.
            if (Profile.Mode == RecoveryMode.Compiler)
                break;

            e = RecoveryPointOf(result, input);
            if (e <= ePrev)
                break; // глобальный предохранитель (fail-safe)
            ePrev = e;
            if (iter >= MaxRecoveryIterations)
                break;

            var snapshot = FailureSnapshotAt(e);
            Log($"S0 reparse at recovery point {e}, snapshot: {(snapshot?.Pos.ToString() ?? "n/a")}", LogImportance.High);
            _recoveryPoint = e;

            if (!_attempts.TryGetValue(e, out var attempts))
            {
                attempts = new TierBudget();
                _attempts[e] = attempts;
            }

            // S0 (ре-парсинг как есть: Hygiene без патчей) — всегда первый; S1..S5 генерируются лениво (Generate — дорого:
            // спекулятивные parse'ы) и только если S0 не дал прогресса. Порядок проб/акцепта не меняется (S0 и так первый).
            var recoveredThisIteration = false;

            bool TryCandidate(RecoveryCandidate candidate)
            {
                if (attempts.TriedIds.Contains(candidate.Id))
                    return false; // уже пробовали на этой точке — следующий кандидат
                attempts.TriedIds.Add(candidate.Id);
                // A2: бюджеты по тирам (не по кандидатам). S0 (ранг 0) — базовый re-парс, S6 (ранг 6) —
                // гарантированное дно (1.3.2): оба вне под-бюджетов (всегда пробуются). Остальные —
                // под-бюджет своего тира: S1 (много вставок) не выедает общий бюджет → S2/S3/S6 не голодают.
                if (candidate.Rank is not 0 and not 6)
                {
                    var count = candidate.Rank switch
                    {
                        1 => attempts.S1,
                        2 => attempts.S2,
                        _ => attempts.S3S6
                    };
                    var budget = candidate.Rank switch
                    {
                        1 => S1TierBudget,
                        2 => S2TierBudget,
                        _ => S3S6TierBudget
                    };
                    if (count >= budget)
                        return false; // под-бюджет тира исчерпан — следующий кандидат (не стопим цикл)
                }
                switch (candidate.Rank)
                {
                    case 1: attempts.S1++; break;
                    case 2: attempts.S2++; break;
                    case 3:
                    case 4:
                    case 5: attempts.S3S6++; break;
                }

                var log = ApplyPatches(candidate, e, snapshot, startRule, currentStartPos); // патчи + Hygiene атомарно, всё в лог
                var savedErrorPos = ErrorPos;
                var savedExpected = _expected.ToArray();
                _recoveryPoint = e;
                var reparseSw = Stopwatch.StartNew(); // D2-full (6.2.2): (c) candidate re-parse
                var next = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
                metrics.NoteReparse(reparseSw.Elapsed);
                var e2 = RecoveryPointOf(next, input);

                // 1.3.4: S6 (ранг 6) — дно, а не восстановление: ошибка в точке e не «починена», а лишь
                // поглощена до EOF (абсорбер). Отчёт и продолжение (A4-2): отметка Unrecovered в точке
                // восстановления e. Gate — candidate.Rank == 6 (S6 — принятый кандидат): цикл пробует
                // кандидатов по порядку и выходит при первом дающем прогресс, поэтому если принят S6 —
                // ни один ремонтный (S0–S5) прогресса не дал. НЕ бюджетная эвристика (attempts.Count).
                void AddUnrecoveredIfS6()
                {
                    if (candidate.Rank != 6)
                        return;
                    _recoveryDiagnostics.Add(new RecoveryDiagnostic(e, e, RecoveryKind.Unrecovered, $"error at {e} not recovered (absorbed to EOF)", null, startRule));
                }

                // A4-6 (6.3.1): a recovery was accepted in the failing rule — record its distinct
                // recovery point (the snapshot top frame's Location). Observation-only (signal 2:
                // "all recovery points of a rule identical"). Only the top frame is the failing rule;
                // a null/empty snapshot (e.g. S6 with no snapshot) has no rule to attribute.
                void NoteRecoveryPoint()
                {
                    if (snapshot is { Stack.Length: > 0 })
                    {
                        var top = snapshot.Stack[^1];
                        GrammarDiagnostics.NoteRecoveryPoint(top.RuleName, top.Location);
                    }
                }

                if (next.TryGetSuccess(out _, out var end2) && end2 == input.Length)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    AddUnrecoveredIfS6();
                    NoteRecoveryPoint(); // A4-6 (6.3.1): grammar-quality recovery point
                    result = next;
                    _quietZoneEnd = e;
                    recoveredThisIteration = true;
                    metrics.NoteAccept(candidate.Rank); // D2-full (6.2.1): кандидат стратегии принят
                    return true; // полностью восстановлено
                }
                if (e2 > e)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    AddUnrecoveredIfS6();
                    NoteRecoveryPoint(); // A4-6 (6.3.1): grammar-quality recovery point
                    result = next;
                    _quietZoneEnd = e;
                    recoveredThisIteration = true;
                    metrics.NoteAccept(candidate.Rank); // D2-full (6.2.1): кандидат стратегии дал прогресс
                    return true; // I1: прогресс — к следующему итеративному проходу
                }
                metrics.NoteRollback(candidate.Rank); // D2-full (6.2.1): кандидат отклонён — откат
                RollbackPatches(log); // нет прогресса — откат (включая Hygiene), следующий кандидат
                ErrorPos = savedErrorPos;
                _expected = new HashSet<Terminal>(savedExpected);
                return false;
            }

            if (!TryCandidate(CandidateS0(e, snapshot, startRule)))
            {
                // B4.4: hard limit — force the S6 floor (absorber) and stop. Coarse recovery, not FatalError.
                if (ForceS6)
                {
                    var pe = result.TryGetSuccess(out _, out var p1) ? p1 : result.TryGetPartial(out _, out var p2) ? p2 : 0;
                    var forceGenSw = Stopwatch.StartNew(); // D2-full (6.2.2): (b) generation
                    var forceCandidates = Recovery.RecoveryEngine.Generate(e, snapshot, input, this, result.ResultKind, startRule, currentStartPos, pe);
                    metrics.NoteGeneration(forceGenSw.Elapsed);
                    foreach (var c in forceCandidates)
                        if (c.Rank == 6)
                        {
                            TryCandidate(c);
                            break;
                        }
                    break; // stop the recovery loop: hard limit reached
                }

                EngineGenerateCalls++;
                var parseEnd = result.TryGetSuccess(out _, out var pe2) ? pe2 : result.TryGetPartial(out _, out var pp2) ? pp2 : 0;
                var genSw = Stopwatch.StartNew(); // D2-full (6.2.2): (b) generation
                var candidates = Recovery.RecoveryEngine.Generate(e, snapshot, input, this, result.ResultKind, startRule, currentStartPos, parseEnd);
                metrics.NoteGeneration(genSw.Elapsed);
                foreach (var candidate in candidates)
                    if (TryCandidate(candidate))
                        break;
            }

            if (result.TryGetSuccess(out _, out var endAfter) && endAfter == input.Length)
                return FinishRecovery(result); // полностью восстановлено

            if (!recoveredThisIteration)
                break; // кандидаты исчерпаны — ошибка неотвратима
        }

        return FinishRecovery(result);
    }

    // A5-2 (6.1.2b): завершение recovery-сессии (механизм B): каждая накопленная диагностика
    // прикрепляется к узлу финального дерева, которому она соответствует, — DeriveRecoveryDiagnostics(root)
    // воспроизводит kind (D1: S1b → Extraneous, а не Skipped) и метаданные Terminal/RuleName (D4)
    // накопленного списка. Точка — КОНЕЦ сессии (не момент акцепта кандидата): итеративный re-парс
    // пересоздаёт инъекционные узлы как новые инстанции (а memo-патченные узлы S2/S3/S6 живут через
    // memo) — единый матч по финальному дереву покрывает оба вида одинаково.
    private Result FinishRecovery(Result result)
    {
        // D2-full (6.2.2): record the per-session hygiene removals (the single live counter — post-B1
        // exact hygiene; HygieneCore increments HygieneRemovals, reset per Recover). Single session
        // exit point: every Recover return goes through here.
        Metrics.HygieneRemovalsAfter = HygieneRemovals;
        if (result.TryGetSuccess(out var node, out _))
        {
            AttachDiagnosticsToTree(node);
            FinalizeRecoveryDiagnostics(node);
            return result;
        }
        if (result.TryGetPartial(out var pnode, out _))
        {
            AttachDiagnosticsToTree(pnode);
            FinalizeRecoveryDiagnostics(pnode);
            return result;
        }
        FinalizeRecoveryDiagnostics(null);
        return result;
    }

    // A5-2 (6.1.2c): build the public list from the final tree. DeriveRecoveryDiagnostics(node) returns
    // the node-attached diagnostics — the EXACT accumulated instances, in tree position order — and the
    // Unrecovered marker(s) are appended from the _recoveryDiagnostics cache (Unrecovered is not a tree
    // node, A4-2, so it is not tree-derived; the recovery loop's e only increases, so they are already in
    // position order and append cleanly after the derived part). node == null (a Failure result) → no
    // tree; in practice the cache holds no Unrecovered then (a Failure means no candidate was ever
    // accepted), so the list is empty.
    private void FinalizeRecoveryDiagnostics(ISyntaxNode? node)
    {
        var derived = node is null
            ? new List<RecoveryDiagnostic>()
            : DeriveRecoveryDiagnostics(node).ToList();
        derived.AddRange(_recoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered));
        // 7.1.2/R3: guard-fired result = A5-4 bottom contract + ONE InsufficientStack diagnostic.
        // The bottom contract (Success<T> with a tree covering the input) already holds via
        // recovery-to-EOF/S6; here we add the result-level marker (like Unrecovered — not a tree
        // node, so it is appended, not tree-derived) at the point where the depth guard first
        // rejected a frame (the farthest reached position). Emitted once per Parse (this is the
        // single finalization point reached on every FinishRecovery exit).
        if (_guardFired)
            derived.Add(new RecoveryDiagnostic(_guardFiredPos, _guardFiredPos, RecoveryKind.InsufficientStack, $"insufficient execution stack (depth guard fired at pos {_guardFiredPos})", null, null));
        _finalRecoveryDiagnostics = derived;
    }

    // A5-2 (6.1.2b): корреляция узел↔диагностика по позиции+форме — надёжна для всех стратегий:
    //   • диагностика с регионом (StartPos < EndPos) — абсорбер (S1b/S2/S3/S5/S6): IsRecovery +
    //     IsAbsorber узел с точным пролётом [StartPos..EndPos) (уникален: на точку восстановления
    //     принимается один кандидат, точки строго возрастают);
    //   • нулевая диагностика (StartPos == EndPos) — вставка (S1/S2/S4): нулевой IsRecovery узел в
    //     StartPos; если диагностика несёт Terminal — только узел с тем же Kind (инъекция создаёт
    //     узел с NodeKind == t.Kind, CreateInjectedResult);
    //   • Unrecovered — не узел дерева (нулевой маркер в точке восстановления, A4-2) — не матчится.
    // Мягкий разделитель (D2) вне матча — его узел обычный терминал (6.1.2b2).
    private void AttachDiagnosticsToTree(ISyntaxNode root)
    {
        if (_recoveryDiagnostics.Count == 0)
            return;

        var nodes = new List<TerminalNode>();
        CollectRecoveryNodes(root, nodes);

        foreach (var diag in _recoveryDiagnostics)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (MatchesRecoveryNode(nodes[i], diag))
                {
                    AttachDiagnostic(nodes[i], diag);
                    break;
                }
            }
        }
    }

    // Обход полного дерева (RawElements — включая абсорберы): собирает IsRecovery-терминалы.
    private static void CollectRecoveryNodes(ISyntaxNode node, List<TerminalNode> result)
    {
        switch (node)
        {
            case TerminalNode { IsRecovery: true } terminal:
                result.Add(terminal);
                break;
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    CollectRecoveryNodes(el, result);
                break;
            case ListNode ln:
                foreach (var el in ln.RawElements)
                    CollectRecoveryNodes(el, result);
                foreach (var d in ln.Delimiters)
                    CollectRecoveryNodes(d, result);
                break;
            case SomeNode some:
                CollectRecoveryNodes(some.Value, result);
                break;
        }
    }

    private static bool MatchesRecoveryNode(TerminalNode node, RecoveryDiagnostic diag)
    {
        if (diag.Kind is RecoveryKind.Unrecovered)
            return false;
        if (diag.StartPos < diag.EndPos)
            return node.IsAbsorber && node.StartPos == diag.StartPos && node.EndPos == diag.EndPos;
        return !node.IsAbsorber
            && node.StartPos == diag.StartPos
            && node.EndPos == diag.StartPos
            && (diag.Terminal is null || node.Kind == diag.Terminal.Kind);
    }

    // Спекулятивный parse: подавление побочных эффектов (снимки/ErrorPos/_expected) на время parse и откат.
    private partial Result Speculative(Func<Result> parse)
    {
        var savedErrorPos = ErrorPos;
        var savedExpected = _expected.ToArray();
        var savedSnapshot = _lastSnapshot;
        _suppressSideEffects = true;
        try
        {
            return parse();
        }
        finally
        {
            _suppressSideEffects = false;
            ErrorPos = savedErrorPos;
            _expected = new HashSet<Terminal>(savedExpected);
            _lastSnapshot = savedSnapshot;
        }
    }

    // ============ MemoPatch-лог (1.3): применение патчей кандидата + Hygiene атомарно, откат по логy ============

    private void BeginPatchLog()
    {
        _memoPatchLog = new List<MemoPatch>();
        _injectionPatchLog = new List<InjectionPatch>();
    }

    private PatchLog EndPatchLog()
    {
        var log = new PatchLog(_memoPatchLog!, _injectionPatchLog!);
        _memoPatchLog = null;
        _injectionPatchLog = null;
        return log;
    }

    private void RecordMemo((int pos, string rule, int precedence) key)
    {
        if (_memoPatchLog is not { } log)
            return;
        var had = _memo.TryGetValue(key, out var old);
        log.Add(new MemoPatch(key, had ? old : null));
    }

    private void RecordMemoRemove((int pos, string rule, int precedence) key)
    {
        if (_memoPatchLog is not { } log)
            return;
        var had = _memo.TryGetValue(key, out var old);
        log.Add(new MemoPatch(key, had ? old : null));
    }

    private void RecordInjection((int pos, Terminal Terminal) key)
    {
        if (_injectionPatchLog is not { } log)
            return;
        var had = _injections.TryGetValue(key, out var old);
        log.Add(new InjectionPatch(key, had ? old : null));
    }

    // Патчи кандидата + Hygiene в одном атомарном логy (всё для отката). Интеграция цикла (1.3).
    // Hygiene ДО Apply: S6-дно (A1) memo-патчит start-правило на currentStartPos — если Hygiene шёл бы после,
    // он удалил бы этот патч (scope b) и ре-парс не увидел бы Success@EOF. S1-S5 патчат другие позиции, для них порядок неважен.
    public PatchLog ApplyPatches(RecoveryCandidate candidate, int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        HygieneCore(e, snapshot, startRule, currentStartPos);
        candidate.Apply(this);
        return EndPatchLog();
    }

    // Hygiene в отдельном логy (для тестов): см. HygieneCore.
    public PatchLog Hygiene(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        HygieneCore(e, snapshot, startRule, currentStartPos);
        return EndPatchLog();
    }

    // Гигиена memo — ТОЧНАЯ (2.1/B1). Удаляются только записи, реально невалидные в точке восстановления E:
    //  (f) Failure с pos < E && MaxFailPos >= E: падение, начавшееся ДО E и дошедшее до E (или дальше) — его
    //      вычисление опиралось на вход в окрестности E, а re-парсинг меняет его инъекцией/абсорбером → невалидно.
    //      Failure с MaxFailPos < E (целиком в доверенном префиксе) и Failure с pos >= E — не трогаем.
    //  (b) start-правило на currentStartPos ЛЮБОГО типа (Partial/Success < EOF): устаревший верхнеуровневый результат
    //      первого прохода — иначе re-парсинг читает его и не переиспытывает start-правило (тесты SeparatedList/ErrorEmpty).
    // Success/Partial никогда не удаляются (I2: валидные факты префикса). Требует активный лог патчей.
    private void HygieneCore(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        for (var p = currentStartPos; p <= e; p++)
        {
            if (!_memoByPos.TryGetValue(p, out var keys))
                continue;
            foreach (var key in keys.ToArray())
            {
                if (!_memo.TryGetValue(key, out var value))
                    continue;
                var isStartRuleAtCurrentStart = key.pos == currentStartPos && key.rule == startRule;
                var isInvalidFailure = value.ResultKind == Result.Kind.Failure && key.pos >= currentStartPos && key.pos <= e;
                if (isStartRuleAtCurrentStart || isInvalidFailure)
                {
                    RemoveMemo(key.rule, key.pos, key.precedence);
                    HygieneRemovals++;
                }
            }
        }
    }

    // Откат логов (обратный порядок): возвращает OldValue (или удаляет ключ, если OldValue == null).
    public void RollbackPatches(PatchLog log)
    {
        for (var i = log.Memo.Count - 1; i >= 0; i--)
        {
            var patch = log.Memo[i];
            if (patch.OldValue is { } old)
            {
                _memo[patch.Key] = old;
                IndexMemoAdd(patch.Key);
            }
            else
            {
                _memo.Remove(patch.Key);
                IndexMemoRemove(patch.Key);
            }
        }

        for (var i = log.Injection.Count - 1; i >= 0; i--)
        {
            var patch = log.Injection[i];
            if (patch.OldValue is { } old)
                _injections[patch.Key] = old;
            else
                _injections.Remove(patch.Key);
        }
    }

    // С0 — неявный кандидат «ре-парсинг как есть»: без патчей (Hygiene применяется в ApplyPatches). Всегда первый.
    private RecoveryCandidate CandidateS0(int e, FailureSnapshot? snapshot, string startRule) =>
        new("S0", 0, e, 0, startRule, null, _ => { }, _ => { }, []);

    // В 1.1 снимок — заготовка для engine'а (1.2): возвращаем _lastSnapshot, если он снят в точке e,
    // иначе null (синтетический снимок для хвостового мусора появится в 1.2/S5).
    private FailureSnapshot? FailureSnapshotAt(int e)
    {
        var snapshot = _lastSnapshot;
        return snapshot is not null && snapshot.Pos == e ? snapshot : null;
    }

    private void CaptureSnapshot(int pos, Terminal failedTerminal)
    {
        var stack = _stackFrames.ToArray();
        var expected = new List<Terminal>();
        if (stack.Length > 0)
        {
            var topExpected = stack[stack.Length - 1].Expected;
            if (topExpected is not null)
                expected.AddRange(topExpected);
        }
        if (!expected.Contains(failedTerminal))
            expected.Add(failedTerminal);
        _lastSnapshot = new FailureSnapshot(pos, stack, failedTerminal, expected.ToArray());
    }

    // A2: состояние под-бюджетов тиров на одной точке восстановления. S0 (ранг 0) и S6 (ранг 6)
    // вне под-бюджетов; счётчики S1/S2/S3S6 — число кандидатов тира, уже пробованных на точке.
    private sealed class TierBudget
    {
        public readonly HashSet<string> TriedIds = new();
        public int S1;
        public int S2;
        public int S3S6;
    }
}
