namespace Regex;

public static class DfaInterpreter
{
    public static int TryMatch(DfaState start, string input, int startPos = 0, Log? log = null)
    {
        log?.Info($"\nStarting matching for pattern '{start}' against input '{input}' from position {startPos}");

        var current = start;
        int maxLen = current.IsFinal ? 0 : -1;
        int currentLen = 0;

        log?.Info($"Initial state: {current.Id}, IsFinal: {current.IsFinal}, Current maxLen: {maxLen}");

        for (int i = startPos; i < input.Length; i++)
        {
            char c = input[i];
            bool matched = false;
            log?.Info($"\nProcessing char '{c}' at position {i}");

            foreach (var transition in current.Transitions)
            {
                bool conditionMatched = MatchesCondition(transition.Condition, c, log);
                log?.Info($"Checking transition: {transition.Condition} -> State {transition.Target.Id}, Matched: {conditionMatched}");

                if (conditionMatched)
                {
                    current = transition.Target;
                    matched = true;
                    currentLen++;
                    log?.Info($"Moving to state {current.Id}, IsFinal: {current.IsFinal}, Current length: {currentLen}");

                    if (current.IsFinal)
                    {
                        maxLen = currentLen;
                        log?.Info($"New final state reached, updating maxLen to {maxLen}");
                    }
                    break;
                }
            }

            if (!matched)
            {
                log?.Info($"No matching transitions found for char '{c}', resetting search");
                break;
            }
        }

        // Если не было ни одного совпадения, но начальное состояние финальное
        if (maxLen == -1 && start.IsFinal)
        {
            maxLen = 0;
            log?.Info($"No characters matched, but start state is final. Setting maxLen to 0");
        }

        log?.Info($"\nFinal result: maxLen = {maxLen}");
        return maxLen;
    }

    private static bool MatchesCondition(RegexNode condition, char c, Log? log)
    {
        bool result = condition switch
        {
            NegatedCharClassGroup group => group.Matches(c),
            RegexChar rc => rc.Value == c,
            RegexAnyChar => true,
            RegexCharClass cc => cc.Matches(c),
            _ => false
        };
        log?.Info($"Matching condition {condition} with char '{c}': {result}");
        return result;
    }
}
