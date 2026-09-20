# T: ComputeSpans — cheap extent

## T1: ordinary + verbatim linear scan

### Deliberate deviation: literal extent vs Expression extent

`ComputeSpans` now uses a linear scan that returns the **literal extent** (from
opening quote to closing quote), not the **Expression extent** that
`ParseExpressionLength` returns. The parser extends the extent past the literal
to include trailing operators (`,`, `>`, `+`, etc.) and trivia (whitespace,
newlines) because it parses the full TDOPP `Expression` rule.

**All 137 mismatches on the test corpus have `parser_extent > literal_extent`**
(no reverse). Observable behavior (`IsInsideSpan`) is identical on all existing
test inputs: the differing regions contain no line-start `#`.

This is semantically more correct: `IsInsideSpan` should detect whether a `#`
is inside a string **literal**, not inside an expression that happens to
contain a literal. The original code's comment acknowledged the fragility of
using `ParseExpressionLength` for this purpose.

### Test 2

Before T1: 14.8s. After T1: ~10.0s (32% improvement; 40,000 of ~50,000 parser
calls eliminated). Remaining cost: 10,000 raw-string `ParseExpressionLength`
calls (T2) + fixed line processing.
