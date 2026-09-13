#nullable enable

using Diagnostics;

namespace ExtensibleParser;

using ExtensibleParser.Recovery;

// Recovery-подсистема: состояние, хуки и цикл восстановления.
// В сборке EnableRecovery=false этот файл не компилируется — см. Parser.NoRecovery.cs.
public partial class Parser
{
    private int _recoveryPoint = -1;
    private FailureSnapshot? _lastSnapshot;
    private bool _suppressSideEffects;
    public FailureSnapshot? LastSnapshot => _lastSnapshot;
    private Result? _lastPartial;
    public Result? LastPartial => _lastPartial;
    private readonly List<RecoveryDiagnostic> _recoveryDiagnostics = [];
    public IReadOnlyList<RecoveryDiagnostic> RecoveryDiagnostics => _recoveryDiagnostics;

    // Хук для тестов (паттерн LastSnapshot/LastPartial): число ленивых вызовов RecoveryEngine.Generate в последнем Parse.
    public int EngineGenerateCalls { get; private set; }

    // Хук для тестов (2.4, бенчмарк): число проходов recovery-цикла (итераций) в последнем Parse.
    // Сбрасывается в начале Parse, инкрементируется на каждой итерации цикла восстановления.
    public int RecoveryPasses { get; private set; }

    // Лимиты цикла восстановления: предельное число итераций и число попыток кандидатов на одной точке восстановления.
    public int MaxRecoveryIterations { get; set; } = 64;

    // Дефолт 3 — «предохранитель» по плану (§3.1, I3): ограничивает число попыток кандидатов на одной
    // точке восстановления. Глубокие сценарии (E2E: resync/panic/trailing, ранги S2/S3/S5 = 11-я+ попытка)
    // ставят бюджет ЯВНО (parser.MaxRecoveryAttemptsPerPosition = 16) — глобальный подъём дефолта ломает
    // консервативное поведение существующих тестов (загрязнение _expected / восстановление вопреки
    // Recoverable=false), проверено (3.0b). 3.0a гарантирует, что подъём бюджета не даёт краша.
    public int MaxRecoveryAttemptsPerPosition { get; set; } = 3;
    private readonly Dictionary<int, HashSet<string>> _attempts = new();

    // Логи патчей текущего кандидата (memo и инъекции): заполняются хуками, пока активны, — основа отката.
    private List<MemoPatch>? _memoPatchLog;
    private List<InjectionPatch>? _injectionPatchLog;

    // ============ Реализации хуков (декларации — в Parser.cs) ============

    private partial void OnMismatch(Terminal terminal, int pos) => CaptureSnapshot(pos, terminal);
    private partial void OnMemoWritten((int pos, string rule, int precedence) key) => RecordMemo(key);
    private partial void OnMemoRemoved((int pos, string rule, int precedence) key) => RecordMemoRemove(key);
    private partial void OnInjectionApplied((int Pos, Terminal Terminal) key) => RecordInjection(key);
    private partial void OnPartialCaptured(Result partialResult) => _lastPartial = partialResult;
    private partial bool IsRecoveryPosition(int pos) => pos == _recoveryPoint;
    private partial bool SuppressSideEffects => _suppressSideEffects;
    private partial void ResetRecoveryPoint() => _recoveryPoint = -1;

    // Один parse (без fail-fast): setup recovery-состояния + цикл восстановления (S0 + ленивый Generate).
    private partial Result Recover(string input, string startRule, int currentStartPos)
    {
        _recoveryDiagnostics.Clear();
        _attempts.Clear();
        EngineGenerateCalls = 0;
        RecoveryPasses = 0;
        _recoveryPoint = -1;
        _lastSnapshot = null;
        _lastPartial = null;
        _suppressSideEffects = false;

        var ePrev = -1;
        var e = -1;
        var result = default(Result);

        for (var iter = 0; ; iter++)
        {
            RecoveryPasses++;
            Log($"Starting at {currentStartPos} iter={iter} parse for rule '{startRule}' _recoveryPoint={_recoveryPoint}");
            result = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);

            if (result.TryGetSuccess(out _, out var end) && end == input.Length)
                return result; // чистый успех

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
                attempts = new HashSet<string>();
                _attempts[e] = attempts;
            }

            // S0 (ре-парсинг как есть: Hygiene без патчей) — всегда первый; S1..S5 генерируются лениво (Generate — дорого:
            // спекулятивные parse'ы) и только если S0 не дал прогресса. Порядок проб/акцепта не меняется (S0 и так первый).
            var recoveredThisIteration = false;

            bool TryCandidate(RecoveryCandidate candidate)
            {
                if (attempts.Contains(candidate.Id))
                    return false; // уже пробовали на этой точке — следующий кандидат
                attempts.Add(candidate.Id);
                if (attempts.Count > MaxRecoveryAttemptsPerPosition)
                    return true; // лимит попыток на точке исчерпан — стоп (не акцепт: recoveredThisIteration не тронут)

                var log = ApplyPatches(candidate, e, snapshot, startRule, currentStartPos); // патчи + Hygiene атомарно, всё в лог
                var savedErrorPos = ErrorPos;
                var savedExpected = _expected.ToArray();
                _recoveryPoint = e;
                var next = ParseRule(startRule, minPrecedence: 0, startPos: currentStartPos, input);
                var e2 = RecoveryPointOf(next, input);

                if (next.TryGetSuccess(out _, out var end2) && end2 == input.Length)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    result = next;
                    recoveredThisIteration = true;
                    return true; // полностью восстановлено
                }
                if (e2 > e)
                {
                    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
                    result = next;
                    recoveredThisIteration = true;
                    return true; // I1: прогресс — к следующему итеративному проходу
                }
                RollbackPatches(log); // нет прогресса — откат (включая Hygiene), следующий кандидат
                ErrorPos = savedErrorPos;
                _expected = new HashSet<Terminal>(savedExpected);
                return false;
            }

            if (!TryCandidate(CandidateS0(e, snapshot, startRule)))
            {
                EngineGenerateCalls++;
                foreach (var candidate in Recovery.RecoveryEngine.Generate(e, snapshot, input, this, result.ResultKind))
                    if (TryCandidate(candidate))
                        break;
            }

            if (result.TryGetSuccess(out _, out var endAfter) && endAfter == input.Length)
                return result; // полностью восстановлено

            if (!recoveredThisIteration)
                break; // кандидаты исчерпаны — ошибка неотвратима
        }

        return result;
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
    public PatchLog ApplyPatches(RecoveryCandidate candidate, int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        candidate.Apply(this);
        HygieneCore(e, snapshot, startRule, currentStartPos);
        return EndPatchLog();
    }

    // Hygiene в отдельном логy (для тестов): см. HygieneCore.
    public PatchLog Hygiene(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        BeginPatchLog();
        HygieneCore(e, snapshot, startRule, currentStartPos);
        return EndPatchLog();
    }

    // Гигиена memo. Узкий вариант спеки §3.5 (только Failure на e для правил снимка + Failure start-правила
    // на currentStartPos) не проходит recovery-тесты (см. чек-лист 1.3): Error-правила/OftenMissed переиспытывают
    // правила, упавшие в первом проходе на РАЗНЫХ позициях, а не только на e. Минимальное обоснованное расширение:
    //  (c) ВСЕ stale Failure (любая позиция) — только Failure, не Success/Partial (I2: Success/Partial — валидные
    //      факты префикса; применённые патчи — Success, заканчивающиеся в e — не трогаем).
    //  (b) start-правило на currentStartPos ЛЮБОГО типа (Partial/Success < EOF): устаревший верхнеуровневый результат
    //      первого прохода — иначе re-парсинг читает его и не переиспытывает start-правило (тесты SeparatedList/ErrorEmpty).
    // (a) Failure на e для правил снимка — подмножество (c).
    // I2 нарушается только для Failure в префиксе [currentStartPos, e) и для записи start-правила — не удаётся избежать. Требует активный лог патчей.
    private void HygieneCore(int e, FailureSnapshot? snapshot, string startRule, int currentStartPos)
    {
        foreach (var key in _memo.Keys.ToList())
            if (key.pos == currentStartPos && key.rule == startRule || _memo[key].ResultKind == Result.Kind.Failure)
                RemoveMemo(key.rule, key.pos, key.precedence);
    }

    // Откат логов (обратный порядок): возвращает OldValue (или удаляет ключ, если OldValue == null).
    public void RollbackPatches(PatchLog log)
    {
        for (var i = log.Memo.Count - 1; i >= 0; i--)
        {
            var patch = log.Memo[i];
            if (patch.OldValue is { } old)
                _memo[patch.Key] = old;
            else
                _memo.Remove(patch.Key);
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
}
