using System.Runtime.CompilerServices;

namespace Regex;

public class DfaBuilder(Log? log = null)
{
    public DfaState Build(NfaState start)
    {
        log?.Info("\n==== Building DFA ====");

        var nfaStates = GetAllStates(start);
        log?.Info($"Total NFA states: {nfaStates.Count}");

        var queue = new Queue<HashSet<NfaState>>();
        var mapping = new Dictionary<HashSet<NfaState>, DfaState>(HashSet<NfaState>.CreateSetComparer());

        var initial = EpsilonClosure(new[] { start });
        log?.Info($"Initial ε-closure: [{string.Join(", ", initial.Select(s => s.Id))}]");

        bool isInitialFinal = initial.Any(s => s.IsFinal) && !IsEmptyPattern(start);
        var dfaStart = new DfaState(0, isInitialFinal);
        mapping[initial] = dfaStart;
        queue.Enqueue(initial);

        log?.Info($"Created initial DFA state 0 (IsFinal: {dfaStart.IsFinal})");

        while (queue.Count > 0)
        {
            var currentSet = queue.Dequeue();
            var currentDfaState = mapping[currentSet];

            log?.Info($"\nProcessing DFA state {currentDfaState.Id}:");
            log?.Info($"NFA states: [{string.Join(", ", currentSet.Select(s => s.Id))}]");

            var transitions = GetTransitions(currentSet);
            log?.Info($"Found {transitions.Count} transitions");

            foreach (var (condition, targets) in transitions)
            {
                var closure = EpsilonClosure(targets);
                log?.Info($"  Condition: {condition}");
                log?.Info($"  Targets: [{string.Join(", ", closure.Select(s => s.Id))}]");

                if (!mapping.TryGetValue(closure, out var dfaState))
                {
                    bool isFinal = closure.Any(s => s.IsFinal);
                    dfaState = new DfaState(mapping.Count, isFinal);
                    mapping[closure] = dfaState;
                    queue.Enqueue(closure);

                    log?.Info($"  Created new DFA state {dfaState.Id} (IsFinal: {isFinal})");
                }
                else
                {
                    log?.Info($"  Reusing existing DFA state {dfaState.Id}");
                }

                currentDfaState.Transitions.Add(new DfaTransition(condition, dfaState));
                log?.Info($"  Added transition: {condition} → State {dfaState.Id}");
            }
        }

        log?.Info($"\nDFA construction complete. Total states: {mapping.Count}");
        return dfaStart;
    }

    private bool IsEmptyPattern(NfaState start)
    {
        bool isEmpty = start.IsFinal && start.Transitions.Count == 0;
        log?.Info($"Checking empty pattern: {isEmpty}");
        return isEmpty;
    }

    private Dictionary<RegexNode, HashSet<NfaState>> GetTransitions(HashSet<NfaState> states)
    {
        var transitions = new Dictionary<RegexNode, HashSet<NfaState>>(new RegexNodeComparer());

        foreach (var state in states)
        {
            foreach (var t in state.Transitions.Where(t => t.Condition != null))
            {
                var key = t.Condition!;
                if (!transitions.TryGetValue(key, out var set))
                {
                    transitions[key] = set = new HashSet<NfaState>();
                    log?.Info($"New transition condition: {key}");
                }
                set.Add(t.Target);
            }
        }
        return transitions;
    }

    private HashSet<NfaState> EpsilonClosure(IEnumerable<NfaState> start)
    {
        var closure = new HashSet<NfaState>();
        var queue = new Queue<NfaState>(start);

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (!closure.Add(state)) continue;

            foreach (var t in state.Transitions.Where(t => t.Condition == null))
            {
                queue.Enqueue(t.Target);
                log?.Info($"Adding ε-transition: {state.Id} → {t.Target.Id}");
            }
        }
        return closure;
    }

    private HashSet<NfaState> GetAllStates(NfaState start)
    {
        var visited = new HashSet<NfaState>();
        var queue = new Queue<NfaState>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (!visited.Add(state)) continue;

            foreach (var t in state.Transitions)
            {
                queue.Enqueue(t.Target);
                log?.Info($"Tracking NFA state: {t.Target.Id}");
            }
        }
        return visited;
    }

    private class RegexNodeComparer : IEqualityComparer<RegexNode>
    {
        public bool Equals(RegexNode? x, RegexNode? y) =>
            x?.ToString() == y?.ToString();

        public int GetHashCode(RegexNode obj) =>
            obj.ToString().GetHashCode();
    }
}