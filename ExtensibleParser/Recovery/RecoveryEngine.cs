namespace ExtensibleParser.Recovery;

/// <summary>
/// Чистый генератор кандидатов восстановления (§3.4): по точке восстановления e и снимку
/// порождает детерминированный отсортированный список патчей (S1..S5).
/// Интеграция в цикл (apply/rollback при ре-парсинге) — 1.3.
/// </summary>
public static class RecoveryEngine
{
    private const int DefaultMaxSkip = 1000;

    public static List<RecoveryCandidate> Generate(int e, FailureSnapshot? snapshot, string input, Parser parser, Result.Kind resultKind)
    {
        var candidates = new List<RecoveryCandidate>();

        if (snapshot is not null)
        {
            GenerateS1(e, snapshot, input, parser, candidates);
            GenerateS2(e, snapshot, input, parser, candidates);
            GenerateS3(e, snapshot, input, parser, candidates);
            GenerateS4(e, snapshot, input, parser, candidates);
        }

        if (resultKind == Result.Kind.Success && e < input.Length)
            GenerateS5(e, snapshot, input, parser, candidates);

        return Sort(candidates);
    }

    // S1: вставка ожидаемого терминала в точке e (аналог Roslyn EatToken → CreateMissingToken).
    private static void GenerateS1(int e, FailureSnapshot snapshot, string input, Parser parser, List<RecoveryCandidate> candidates)
    {
        var top = snapshot.Stack[^1];
        var ruleName = top.RuleName;
        var calculator = parser.FollowCalculator;
        var ordered = new List<(Terminal T, int Rank)>();

        void Add(Terminal t, int rank)
        {
            if (t is EofTerminal or EpsilonTerminal)
                return;
            if (ordered.Any(x => TerminalComparer.Instance.Equals(x.T, t)))
                return;
            ordered.Add((t, rank));
        }

        Add(snapshot.FailedTerminal, 1);
        foreach (var t in snapshot.Expected)
            Add(t, 1);

        for (var i = snapshot.Stack.Length - 1; i >= 0; i--)
        {
            var options = snapshot.Stack[i].Options;
            // §3.9: кадр с Recoverable=false — опции не используются (строгий контекст, opt-out).
            if (options is { Recoverable: false })
                continue;
            var tryInsert = options?.TryInsert;
            if (tryInsert is null)
                continue;
            foreach (var t in tryInsert)
                Add(t, 0);
            break;
        }

        if (calculator is { } calc)
            foreach (var t in calc.GetFollowSet(top.RuleName))
                Add(t, 1);

        foreach (var (t, rank) in ordered)
        {
            if (t.TryMatch(input, e) >= 0)
                continue;

            var key = (e, t);
            var hadOld = parser.Injections.TryGetValue(key, out var old);
            var injection = Injection.Insert(t.Kind);
            Action<Parser> apply = p => p.ApplyInjection(t, e, injection);
            Action<Parser> rollback = p => p.RollbackInjection(t, e, hadOld ? old : null);

            candidates.Add(new RecoveryCandidate(
                Id: $"S1:{ruleName}:{t.Kind}",
                Rank: rank,
                Pos: e,
                Cost: CostCalculator.InsertCost,
                RuleName: ruleName,
                TerminalKind: t.Kind,
                Apply: apply,
                Rollback: rollback,
                Diagnostics: [new RecoveryDiagnostic(e, e, RecoveryKind.Inserted, $"expected {t.Kind}, found {Preview(input, e)}", t, ruleName)]));
        }
    }

    // S2: resync к known-good точке: T1 — полный спекулятивный parse правила-якоря (скан окончен),
    // T2 — мягкий CanStart-предикат (скан продолжается). Pre-filter по First-множеству.
    // Completion stack: абсорбер [e..S) упавшего элемента (S > e) или нулевая вставка (S == e)
    // + суффиксные обязательства внешних Seq-кадров в точке S.
    private static void GenerateS2(int e, FailureSnapshot snapshot, string input, Parser parser, List<RecoveryCandidate> candidates)
    {
        var calculator = parser.FollowCalculator;
        var maxSkip = GetMaxSkip(snapshot) ?? DefaultMaxSkip;
        var maxS = Math.Min(e + maxSkip, input.Length);
        var top = snapshot.Stack[^1];

        // T1 якоря: авторские (ближайший кадр с записанным полем) + выводимые из циклов Loop-кадров.
        var anchors = new List<Ref>();
        var authorAnchors = NearestOptions(snapshot, f => f.Options?.Anchors);
        if (authorAnchors is not null)
            foreach (var a in authorAnchors)
                if (a is Ref r)
                    anchors.Add(r);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = snapshot.Stack.Length - 1; i >= 0; i--)
        {
            var frame = snapshot.Stack[i];
            if (frame.Location is not LoopFrameLocation)
                continue;
            foreach (var anchor in DeriveLoopAnchors(parser, frame.RuleName))
                if (seen.Add(anchor.RuleName))
                    anchors.Add(anchor);
        }

        // T2 CanStart: только авторские (выводимые не существуют).
        var canStart = new List<Ref>();
        var authorCanStart = NearestOptions(snapshot, f => f.Options?.CanStart);
        if (authorCanStart is not null)
            foreach (var p in authorCanStart)
                if (p is Ref r)
                    canStart.Add(r);

        if (anchors.Count == 0 && canStart.Count == 0)
            return;

        var scratch = CreateScratchParser(parser);
        var specCache = new Dictionary<(string Rule, int Pos), (bool Ok, int EndPos)>();

        (bool Ok, int EndPos) Speculative(string ruleName, int pos)
        {
            var key = (ruleName, pos);
            if (specCache.TryGetValue(key, out var cached))
                return cached;
            var specResult = scratch.ParseRuleOnce(ruleName, 0, pos, input);
            var success = specResult.TryGetSuccess(out _, out var end);
            return specCache[key] = (success, success ? end : -1);
        }

        bool FirstMatchesAt(Ref refRule, int pos)
        {
            foreach (var t in FirstSets.Get(refRule, calculator))
                if (t is not EofTerminal and not EpsilonTerminal && t.TryMatch(input, pos) >= 0)
                    return true;
            return false;
        }

        void AddResyncCandidate(int resyncPos, string tier, string anchorName)
        {
            var penalty = CostCalculator.TierPenalty(tier);
            var insertions = new List<(int Pos, Terminal T, Injection Injection)>();
            var diagnostics = new List<RecoveryDiagnostic>();
            var topIdx = top.Location is SeqFrameLocation { ElementIndex: var ti } ? ti : -1;
            var failedElement = topIdx >= 0
                ? FindSeq(parser, top.RuleName, topIdx)?.Elements[topIdx]
                : null;
            (Action<Parser> Apply, Action<Parser> Rollback)? memoPatch = null;

            if (resyncPos > e)
            {
                // 3.0c: падение в НАЧАЛЕ итерации цикла (topIdx == 0), чьё правило совпадает с якорем
                // resync → абсорбер на уровне цикла: патч memo правила в e, чтобы внешний цикл пропустил
                // регион [e..resyncPos) целиком как одну итерацию и возобновился с чистого терминала.
                if (topIdx == 0 && top.RuleName == anchorName)
                {
                    var absorber = new TerminalNode("Skipped", e, resyncPos, resyncPos - e, IsRecovery: true);
                    var value = Result.Success(absorber, resyncPos, resyncPos);
                    var memoOlds = CaptureMemo(parser, top.RuleName, e);
                    memoPatch = (p => p.PatchMemo(top.RuleName, e, value), p => RollbackMemo(p, top.RuleName, e, memoOlds));
                }
                else if (failedElement is Ref refRule)
                {
                    var absorber = new TerminalNode("Skipped", e, resyncPos, resyncPos - e, IsRecovery: true);
                    var value = Result.Success(absorber, resyncPos, resyncPos);
                    var memoOlds = CaptureMemo(parser, refRule.RuleName, e);
                    memoPatch = (p => p.PatchMemo(refRule.RuleName, e, value), p => RollbackMemo(p, refRule.RuleName, e, memoOlds));
                }
                else
                {
                    var t = failedElement as Terminal ?? snapshot.FailedTerminal;
                    insertions.Add((e, t, Injection.Absorb(t.Kind, resyncPos - e)));
                }
                diagnostics.Add(new RecoveryDiagnostic(e, resyncPos, RecoveryKind.Skipped, $"skip to resync point {resyncPos}", null, top.RuleName));
            }
            else
            {
                var firsts = failedElement is { } fe ? FirstSets.Get(fe, calculator) : [snapshot.FailedTerminal];
                foreach (var t in firsts)
                {
                    if (t is EofTerminal or EpsilonTerminal)
                        continue;
                    if (t.TryMatch(input, e) >= 0)
                        continue;
                    insertions.Add((e, t, Injection.Insert(t.Kind)));
                }
                if (insertions.Count > 0)
                    diagnostics.Add(new RecoveryDiagnostic(e, e, RecoveryKind.Inserted, $"insert at resync point {resyncPos}", null, top.RuleName));
            }

            for (var fi = snapshot.Stack.Length - 2; fi >= 0; fi--)
            {
                var f = snapshot.Stack[fi];
                if (f.Location is not SeqFrameLocation { ElementIndex: var frameIdx })
                    continue;
                var seq = FindSeq(parser, f.RuleName, frameIdx);
                if (seq is null)
                    continue;
                for (var j = frameIdx + 1; j < seq.Elements.Length; j++)
                {
                    var element = seq.Elements[j];
                    if (FirstSets.IsNullable(element, calculator))
                        continue;
                    foreach (var t in FirstSets.Get(element, calculator))
                    {
                        if (t is EofTerminal or EpsilonTerminal)
                            continue;
                        if (t.TryMatch(input, resyncPos) >= 0)
                            continue;
                        insertions.Add((resyncPos, t, Injection.Insert(t.Kind)));
                    }
                }
            }

            var injOlds = CaptureInjections(parser, insertions);
            var apply = memoPatch is { } mp
                ? (Action<Parser>)(p =>
                {
                    mp.Apply(p);
                    ApplyInjections(p, insertions);
                })
                : (Action<Parser>)(p => ApplyInjections(p, insertions));
            var rollback = memoPatch is { } mr
                ? (Action<Parser>)(p =>
                {
                    RollbackInjections(p, injOlds);
                    mr.Rollback(p);
                })
                : (Action<Parser>)(p => RollbackInjections(p, injOlds));

            var cost = (resyncPos > e ? CostCalculator.SkipCost(input, e, resyncPos) : 0) + insertions.Count + penalty;
            candidates.Add(new RecoveryCandidate(
                Id: $"S2:{top.RuleName}:{tier}:{resyncPos}",
                Rank: 2,
                Pos: resyncPos,
                Cost: cost,
                RuleName: top.RuleName,
                TerminalKind: anchorName,
                Apply: apply,
                Rollback: rollback,
                Diagnostics: [.. diagnostics]));
        }

        var foundT1 = false;
        for (var s = e; s <= maxS && !foundT1; s++)
        {
            foreach (var anchor in anchors)
            {
                if (!FirstMatchesAt(anchor, s))
                    continue;
                var (ok, endPos) = Speculative(anchor.RuleName, s);
                if (ok && endPos > s)
                {
                    AddResyncCandidate(s, "T1", anchor.RuleName);
                    foundT1 = true;
                    break;
                }
            }

            if (foundT1)
                break;

            foreach (var p in canStart)
            {
                if (!FirstMatchesAt(p, s))
                    continue;
                var (ok, _) = Speculative(p.RuleName, s);
                if (ok)
                    AddResyncCandidate(s, "T2", p.RuleName);
            }
        }
    }

    // Якоря, выводимые из циклов правила: ZeroOrMany/OneOrMany/SeparatedList с Ref-телом → Ref.
    private static IEnumerable<Ref> DeriveLoopAnchors(Parser parser, string ruleName)
    {
        if (!parser.Rules.TryGetValue(ruleName, out var alternatives))
            yield break;
        foreach (var alt in alternatives)
            foreach (var sub in alt.GetSubRules<Rule>())
            {
                var body = sub switch
                {
                    ZeroOrMany z => z.Element,
                    OneOrMany o => o.Element,
                    SeparatedList sl => sl.Element,
                    _ => null
                };
                if (body is Ref r)
                    yield return r;
            }
    }

    // Ближайший (от верхнего кадра) элемент цикла из снимка — цель абсорбера на уровне цикла (3.0c).
    private static Ref? FindEnclosingLoopElement(Parser parser, FailureSnapshot snapshot)
    {
        for (var i = snapshot.Stack.Length - 1; i >= 0; i--)
        {
            var frame = snapshot.Stack[i];
            if (frame.Location is not LoopFrameLocation)
                continue;
            var first = DeriveLoopAnchors(parser, frame.RuleName).FirstOrDefault();
            if (first is { } r)
                return r;
        }
        return null;
    }

    private static T? NearestOptions<T>(FailureSnapshot snapshot, Func<StackFrame, T?> selector)
        where T : class
    {
        for (var i = snapshot.Stack.Length - 1; i >= 0; i--)
        {
            var frame = snapshot.Stack[i];
            // §3.9: кадр с Recoverable=false — опции не используются (строгий контекст, opt-out).
            if (frame.Options is { Recoverable: false })
                continue;
            var value = selector(frame);
            if (value is not null)
                return value;
        }
        return null;
    }

    // Одноразовый Parser для спекулятивных parse'ов: копия Rules/TdoppRules/Trivia, без recovery-цикла.
    private static Parser CreateScratchParser(Parser parser)
    {
        var scratch = new Parser(parser.Trivia);
        foreach (var kvp in parser.Rules)
            scratch.Rules[kvp.Key] = kvp.Value;
        foreach (var kvp in parser.TdoppRules)
            scratch.TdoppRules[kvp.Key] = kvp.Value;
        return scratch;
    }

    // S3: пропуск до токен-терминатора (panic mode): скан от e+1 до MaxSkip с учётом
    // вложенности пар (чужая закрывающая внутри региона не останавливает скан).
    private static void GenerateS3(int e, FailureSnapshot snapshot, string input, Parser parser, List<RecoveryCandidate> candidates)
    {
        var terminators = parser.GetTerminators(snapshot.Stack);
        var maxSkip = GetMaxSkip(snapshot) ?? DefaultMaxSkip;
        var maxS = Math.Min(e + maxSkip, input.Length);
        var matchCache = new Dictionary<(int Pos, Terminal Terminal), int>(TerminalComparer.KeyComparer);

        int Match(Terminal term, int pos)
        {
            var cacheKey = (pos, term);
            if (matchCache.TryGetValue(cacheKey, out var len))
                return len;
            len = term.TryMatch(input, pos);
            matchCache[cacheKey] = len;
            return len;
        }

        // Оптимизация (2.4): дешёвые терминаторы первыми. Детерминированный порядок:
        // стабильная сортировка по (категория стоимости, позиция в исходном агрегате GetTerminators):
        //   0 — single-char Literal (StartsWith на 1 символ), 1 — остальные Literal, 2 — regex/остальные (DFA).
        // Тир (равная категория) — по исходному порядку агрегата. Если несколько терминаторов совпадают
        // в одной позиции — выигрывает дешевле; при равной категории — тот, что раньше в агрегате.
        // EOF (совпадает только при s == input.Length) вынесен из сортировки и проверяется отдельно.
        var ordered = new List<(Terminal T, int Cost, int OrigIdx)>(terminators.Length);
        var hasEof = false;
        for (var i = 0; i < terminators.Length; i++)
        {
            if (terminators[i] is EofTerminal)
            {
                hasEof = true;
                continue;
            }
            ordered.Add((terminators[i], TerminatorCost(terminators[i]), i));
        }
        ordered.Sort((a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : a.OrigIdx.CompareTo(b.OrigIdx));

        var curly = 0;
        var paren = 0;
        var bracket = 0;
        var foundS = -1;
        Terminal foundT = EofTerminal.Instance;

        for (var s = e + 1; s <= maxS; s++)
        {
            switch (input[s - 1])
            {
                case '{': curly++; break;
                case '}': if (curly > 0) curly--; break;
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
            }

            foreach (var (t, _, _) in ordered)
            {
                if (Match(t, s) < 0)
                    continue;

                var depth = t switch
                {
                    Literal { Value: "}" } => curly,
                    Literal { Value: ")" } => paren,
                    Literal { Value: "]" } => bracket,
                    _ => 0
                };
                if (depth > 0)
                    continue; // чужая закрывающая — скан продолжается

                foundS = s;
                foundT = t;
                break;
            }

            if (foundS < 0 && hasEof && s == input.Length)
            {
                foundS = s;
                foundT = EofTerminal.Instance;
            }

            if (foundS >= 0)
                break;
        }

        if (foundS < 0)
            return;

        var top = snapshot.Stack[^1];
        var topIdx = top.Location is SeqFrameLocation { ElementIndex: var idx } ? idx : -1;
        var failedElement = topIdx >= 0
            ? FindSeq(parser, top.RuleName, topIdx)?.Elements[topIdx]
            : null;
        var cost = CostCalculator.SkipCost(input, e, foundS);
        var diagnostic = new RecoveryDiagnostic(e, foundS, RecoveryKind.Skipped, $"skip to terminator {foundT.Kind}", foundT, top.RuleName);

        // 3.0c: падение в НАЧАЛЕ итерации цикла (topIdx == 0), чьё правило — элемент ближайшего цикла,
        // И терминатор — истинный EOF (foundS == input.Length, хвостовой мусор) → абсорбер на уровне
        // цикла: патч memo правила в e, чтобы цикл пропустил [e..foundS) целиком и дошёл до EOF.
        var loopElement = FindEnclosingLoopElement(parser, snapshot);
        if (topIdx == 0 && foundS == input.Length && loopElement is { } le && top.RuleName == le.RuleName)
        {
            var absorber = new TerminalNode("Skipped", e, foundS, foundS - e, IsRecovery: true);
            var value = Result.Success(absorber, foundS, foundS);
            var olds = CaptureMemo(parser, top.RuleName, e);
            candidates.Add(new RecoveryCandidate(
                Id: $"S3:{top.RuleName}:{foundT.Kind}",
                Rank: 3,
                Pos: foundS,
                Cost: cost,
                RuleName: top.RuleName,
                TerminalKind: foundT.Kind,
                Apply: p => p.PatchMemo(top.RuleName, e, value),
                Rollback: p => RollbackMemo(p, top.RuleName, e, olds),
                Diagnostics: [diagnostic]));
        }
        else if (failedElement is Ref r)
        {
            var absorber = new TerminalNode("Skipped", e, foundS, foundS - e, IsRecovery: true);
            var value = Result.Success(absorber, foundS, foundS);
            var olds = CaptureMemo(parser, r.RuleName, e);
            candidates.Add(new RecoveryCandidate(
                Id: $"S3:{top.RuleName}:{foundT.Kind}",
                Rank: 3,
                Pos: foundS,
                Cost: cost,
                RuleName: top.RuleName,
                TerminalKind: foundT.Kind,
                Apply: p => p.PatchMemo(r.RuleName, e, value),
                Rollback: p => RollbackMemo(p, r.RuleName, e, olds),
                Diagnostics: [diagnostic]));
        }
        else
        {
            var t = failedElement as Terminal ?? snapshot.FailedTerminal;
            var injection = Injection.Absorb(t.Kind, foundS - e);
            var hadOld = parser.Injections.TryGetValue((e, t), out var old);
            candidates.Add(new RecoveryCandidate(
                Id: $"S3:{top.RuleName}:{foundT.Kind}",
                Rank: 3,
                Pos: foundS,
                Cost: cost,
                RuleName: top.RuleName,
                TerminalKind: foundT.Kind,
                Apply: p => p.ApplyInjection(t, e, injection),
                Rollback: p => p.RollbackInjection(t, e, hadOld ? old : null),
                Diagnostics: [diagnostic]));
        }
    }

    private static int? GetMaxSkip(FailureSnapshot snapshot)
    {
        for (var i = snapshot.Stack.Length - 1; i >= 0; i--)
        {
            var options = snapshot.Stack[i].Options;
            // §3.9: кадр с Recoverable=false — опции не используются (строгий контекст, opt-out).
            if (options is { Recoverable: false })
                continue;
            if (options?.MaxSkip is { } maxSkip)
                return maxSkip;
        }
        return null;
    }

    private static List<(int Prec, Result? Old)> CaptureMemo(Parser parser, string rule, int pos)
    {
        var olds = new List<(int Prec, Result? Old)>();
        foreach (var key in parser.Memo.Keys.Where(k => k.pos == pos && k.rule == rule).ToList())
            olds.Add((key.precedence, parser.Memo[key]));
        return olds;
    }

    private static void RollbackMemo(Parser parser, string rule, int pos, List<(int Prec, Result? Old)> olds)
    {
        foreach (var (prec, old) in olds)
            if (old is { } value)
                parser.SetMemo(rule, pos, prec, value);
        foreach (var key in parser.Memo.Keys.Where(k => k.pos == pos && k.rule == rule).ToList())
            if (olds.All(o => o.Prec != key.precedence))
                parser.RemoveMemo(rule, pos, key.precedence);
    }

    // S4: достроение в конце строки — S := input.Length; суффиксные обязательства Seq-кадров
    // (внутренние наружу), только реальные терминалы (EOF не вставляется).
    private static void GenerateS4(int e, FailureSnapshot snapshot, string input, Parser parser, List<RecoveryCandidate> candidates)
    {
        var s = input.Length;
        var calculator = parser.FollowCalculator;
        var insertions = new List<(int Pos, Terminal T, Injection Injection)>();
        var diagnostics = new List<RecoveryDiagnostic>();

        for (var i = snapshot.Stack.Length - 1; i >= 0; i--)
        {
            var frame = snapshot.Stack[i];
            if (frame.Location is not SeqFrameLocation { ElementIndex: var idx })
                continue;
            var seq = FindSeq(parser, frame.RuleName, idx);
            if (seq is null)
                continue;

            for (var j = idx + 1; j < seq.Elements.Length; j++)
            {
                var element = seq.Elements[j];
                if (FirstSets.IsNullable(element, calculator))
                    continue;
                foreach (var t in FirstSets.Get(element, calculator))
                {
                    if (t is EofTerminal or EpsilonTerminal)
                        continue;
                    if (t.TryMatch(input, s) >= 0)
                        continue;
                    insertions.Add((s, t, Injection.Insert(t.Kind)));
                    diagnostics.Add(new RecoveryDiagnostic(s, s, RecoveryKind.Inserted, $"insert {t.Kind} at EOF", t, frame.RuleName));
                }
            }
        }

        if (insertions.Count == 0)
            return;

        var top = snapshot.Stack[^1];
        var olds = CaptureInjections(parser, insertions);
        candidates.Add(new RecoveryCandidate(
            Id: $"S4:{top.RuleName}:EOF",
            Rank: 4,
            Pos: s,
            Cost: insertions.Count,
            RuleName: top.RuleName,
            TerminalKind: null,
            Apply: p => ApplyInjections(p, insertions),
            Rollback: p => RollbackInjections(p, olds),
            Diagnostics: [.. diagnostics]));
    }

    // S5: хвостовой мусор (Success, E < EOF) — один абсорбер [e..EOF) kind "Trailing".
    // Механизм (упрощение 1.2): инъекция Absorb на (e, T), где T — первый терминал правила
    // верхнего кадра (для хвостового мусора — синтетический кадр start-правила, §3.4 S5):
    // ре-парсинг в точке e находит абсорбер и покрывает хвост одним IsRecovery-узлом.
    private static void GenerateS5(int e, FailureSnapshot? snapshot, string input, Parser parser, List<RecoveryCandidate> candidates)
    {
        if (snapshot is null)
            return;

        var top = snapshot.Stack[^1];
        var ruleName = top.RuleName;
        var t = FirstTerminalOfRule(parser, ruleName)
            ?? top.Expected?.FirstOrDefault(x => x is not EofTerminal and not EpsilonTerminal)
            ?? snapshot.FailedTerminal;
        if (t is EofTerminal or EpsilonTerminal)
            return;

        var length = input.Length - e;
        var injection = Injection.Absorb("Trailing", length);
        var key = (e, t);
        var hadOld = parser.Injections.TryGetValue(key, out var old);

        candidates.Add(new RecoveryCandidate(
            Id: $"S5:{ruleName}:Trailing",
            Rank: 5,
            Pos: e,
            Cost: CostCalculator.SkipCost(input, e, input.Length) + 1,
            RuleName: ruleName,
            TerminalKind: "Trailing",
            Apply: p => p.ApplyInjection(t, e, injection),
            Rollback: p => p.RollbackInjection(t, e, hadOld ? old : null),
            Diagnostics: [new RecoveryDiagnostic(e, input.Length, RecoveryKind.Skipped, $"trailing garbage: {length} chars", t, ruleName)]));
    }

    // Первый терминал правила (по первой альтернативе, с разрешением Ref'ов) — цель абсорбера S5.
    private static Terminal? FirstTerminalOfRule(Parser parser, string ruleName)
    {
        if (!parser.Rules.TryGetValue(ruleName, out var alternatives) || alternatives.Length == 0)
            return null;

        var rule = alternatives[0];
        while (true)
        {
            switch (rule)
            {
                case Terminal t:
                    return t;
                case Seq { Elements.Length: > 0 } s:
                    rule = s.Elements[0];
                    break;
                case ZeroOrMany z:
                    rule = z.Element;
                    break;
                case OneOrMany o:
                    rule = o.Element;
                    break;
                case Optional op:
                    rule = op.Element;
                    break;
                case OftenMissed om:
                    rule = om.Element;
                    break;
                case SeparatedList sl:
                    rule = sl.Element;
                    break;
                case AndPredicate a:
                    rule = a.PredicateRule;
                    break;
                case NotPredicate n:
                    rule = n.PredicateRule;
                    break;
                case Ref refRule when parser.Rules.TryGetValue(refRule.RuleName, out var alts) && alts.Length > 0:
                    rule = alts[0];
                    break;
                default:
                    return null;
            }
        }
    }

    // Первый Seq в альтернативах правила, у которого больше elementIndex элементов (упрощение 1.2).
    private static Seq? FindSeq(Parser parser, string ruleName, int elementIndex)
    {
        if (!parser.Rules.TryGetValue(ruleName, out var alternatives))
            return null;
        foreach (var alt in alternatives)
            foreach (var sub in alt.GetSubRules<Seq>())
                if (sub is Seq seq && seq.Elements.Length > elementIndex)
                    return seq;
        return null;
    }

    private static void ApplyInjections(Parser parser, List<(int Pos, Terminal T, Injection Injection)> insertions)
    {
        foreach (var (pos, t, injection) in insertions)
            parser.ApplyInjection(t, pos, injection);
    }

    private static List<(int Pos, Terminal T, Injection? Old)> CaptureInjections(Parser parser, List<(int Pos, Terminal T, Injection Injection)> insertions)
    {
        var olds = new List<(int Pos, Terminal T, Injection? Old)>(insertions.Count);
        foreach (var (pos, t, _) in insertions)
        {
            var had = parser.Injections.TryGetValue((pos, t), out var old);
            olds.Add((pos, t, had ? old : null));
        }
        return olds;
    }

    private static void RollbackInjections(Parser parser, List<(int Pos, Terminal T, Injection? Old)> olds)
    {
        foreach (var (pos, t, old) in olds)
            parser.RollbackInjection(t, pos, old);
    }

    private static string Preview(string input, int pos, int len = 5) => pos >= input.Length
        ? "«»"
        : $"«{input.AsSpan(pos, Math.Min(input.Length - pos, len)).Str()}»";

    // Категория стоимости терминала для S3-скана (2.4): дешёвые первыми.
    // 0 — single-char Literal (StartsWith на 1 символ), 1 — остальные Literal, 2 — regex/остальные (DFA-match).
    private static int TerminatorCost(Terminal t) => t switch
    {
        Literal { Value.Length: 1 } => 0,
        Literal => 1,
        _ => 2,
    };

    // Детерминированный порядок (§3.4.3): (Rank, Cost, Pos, RuleName, TerminalKind).
    private static List<RecoveryCandidate> Sort(List<RecoveryCandidate> candidates) =>
        candidates
            .OrderBy(c => c.Rank)
            .ThenBy(c => c.Cost)
            .ThenBy(c => c.Pos)
            .ThenBy(c => c.RuleName, StringComparer.Ordinal)
            .ThenBy(c => c.TerminalKind ?? string.Empty, StringComparer.Ordinal)
            .ToList();
}
