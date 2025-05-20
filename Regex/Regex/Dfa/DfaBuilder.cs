using System.Runtime.CompilerServices;

namespace Regex;

using Diagnostics;

public class DfaBuilder(Log? log = null)
{
    public DfaState Build(NfaState start)
    {
        log?.Info("\n==== Building DFA ====");

        var nfaStates = GetAllStates(start);
        log?.Info($"Total NFA states: {nfaStates.Count}");

        var queue = new Queue<HashSet<NfaState>>();
        var mapping = new Dictionary<HashSet<NfaState>, DfaState>(HashSet<NfaState>.CreateSetComparer());

        var initial = EpsilonClosure([start]);
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

            log?.Info($"\nProcessing DFA state {currentDfaState}:");

            var transitions = GetTransitions(currentSet);
            log?.Info($"Found {transitions.Count} transitions");

            foreach (var (condition, targets) in transitions)
            {
                log?.Info($"  Condition: {condition} maker epsilon closure");
                var closure = EpsilonClosure(targets);
                log?.Info($"  Condition: {condition} Targets: [{string.Join(", ", closure)}]");

                if (!mapping.TryGetValue(closure, out var dfaState))
                {
                    bool isFinal = closure.Any(s => s.IsFinal);
                    dfaState = new DfaState(mapping.Count, isFinal);
                    if (dfaState.Id == 3)
                    {
                    }
                    mapping[closure] = dfaState;
                    queue.Enqueue(closure);

                    log?.Info($"  Created new DFA state {dfaState}");
                }
                else
                    log?.Info($"  Reusing existing DFA state {dfaState}");

                currentDfaState.Transitions.Add(new DfaTransition(condition, dfaState));
                log?.Info($"  Added transition: {condition} → State {dfaState}");
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
                    transitions.Add(key,  set = new HashSet<NfaState>());

                    if (key.ToString() == "[^[n]]")
                    {
                    }

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
            if (!closure.Add(state))
                continue;

            foreach (var t in state.Transitions.Where(t => t.Condition == null))
            {
                queue.Enqueue(t.Target);
                //if (state.Id == 3  && t.Target.Id is 1 or 2)
                //{
                //}
                log?.Info($"Adding ε-transition: {NfaState.PrintStateId(state)} → {t.Target}");
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
            if (!visited.Add(state))
                continue;

            log?.Info($"NFA state: {state.Id}{(state.IsFinal ? " final" : "")}");

            foreach (var t in state.Transitions)
            {
                queue.Enqueue(t.Target);
                var condition = t.Condition == null ? "ε" : t.Condition.ToString();
                log?.Info($"    Transition: Condition=«{condition}» {t.Target.Id}");
            }
        }
        return visited;
    }

    private class RegexNodeComparer : IEqualityComparer<RegexNode>
    {
        public bool Equals(RegexNode? x, RegexNode? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            // Для CharClass сравниваем не только строковое представление, но и тип
            if (x is RegexCharClass xCharClass && y is RegexCharClass yCharClass)
            {
                return xCharClass.GetType() == yCharClass.GetType()
                    && xCharClass.Negated == yCharClass.Negated
                    && x.ToString() == y.ToString();
            }

            return x.ToString() == y.ToString();
        }

        public int GetHashCode(RegexNode obj)
        {
            unchecked // Переполнение допустимо для хэш-кода
            {
                int hash = 17;
                hash = hash * 23 + obj.ToString().GetHashCode();

                if (obj is RegexCharClass charClass)
                {
                    hash = hash * 23 + charClass.GetType().GetHashCode();
                    hash = hash * 23 + charClass.Negated.GetHashCode();
                }

                return hash;
            }
        }
    }
}
