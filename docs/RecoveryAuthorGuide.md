# Grammar author guide: error recovery

Current contracts for `Parser.Parse` and `RecoveryOptions`. The architectural history is in
`RecoverySystemPlan.v2.md`; current implementation evidence and remaining acceptance work are in
[the October 2026 report](RecoveryCompletionReport-2026-10-10.md).

## Choose the result contract

```csharp
parser.Profile = RecoveryProfile.Ide;      // repaired syntax for an editor
parser.Profile = RecoveryProfile.Compiler; // stop after the initial unsuccessful parse
```

`RecoveryProfile.Test` disables the cooperative time budget for deterministic tests.
In IDE/Test mode, reaching EOF does **not** mean the source was valid. Read `RecoveryDiagnostics`
and inspect recovery nodes before semantic compilation. A recovered `Success` or `Partial` may cover
all input while containing holes or absorbers. Coarse fallback emits `Unrecovered`; a depth guard emits
one `InsufficientStack` marker. A fatal result exposes `ErrorInfo`.

Compiler mode does not generate repairs or accept an incomplete `Partial@EOF` as clean syntax.
The CsNitra grammar AST, JSON AST, DOT, and simplified C++ wrappers use this mode because their semantic
result APIs do not carry recovery diagnostics. The simplified C++ grammar still deliberately skips
uninteresting lines; compiler mode does not make it a full C++ validator.
`ParseSubRule` measures a raw subrule without top-level recovery; it is not a whole-file validity check.

## Annotate the grammar

```csharp
var statement = new RecoveryRule(new Ref("Statement"), new RecoveryOptions
{
    Terminators = new Terminal[] { new Literal(";"), new Literal("}") },
    Anchors = new Rule[] { new Ref("Statement") },
    CanStart = new Rule[] { new Literal("if"), new Literal("return") },
    TryInsert = new Terminal[] { new Literal(";") },
    MaxSkip = 256,
    SoftDepth = 2
});
```

- `Terminators` overrides computed FOLLOW targets at that frame.
- `Anchors` supplies structural resynchronization rules; reference anchors can be speculatively validated.
- `CanStart` supplies declarative soft starts. The engine also derives selected probe predicates from context;
  explicit author hints remain authoritative for the local-garbage shortcut.
- `TryInsert` supplies preferred inserted terminals.
- `MaxSkip` bounds ordinary scans in characters. S6 remains the coarse bottom tier and is exempt from it.
- `SoftDepth` configures the bounded token-count probe used for soft acceptance.
- `Recoverable = false` suppresses ordinary repairs in that region. IDE mode still permits S6 coarse
  source-preserving fallback with `Unrecovered`; it does not imply compiler bail.

`OftenMissed` permits a missing token during recovery. These implicit insertions receive an `Inserted`
diagnostic. `SeparatedList(..., SoftSeparator: terminal)` accepts a foreign separator with a `Skipped`
diagnostic; its default `null` leaves ordinary separator behavior intact.

## Register lexical and nesting boundaries

S3 and S6 share a nesting-aware scanner. With no registrations, `()`, `[]`, and `{}` are used.
The first custom pair switches to the registered set; register conventional pairs too if they remain
part of your language. Registrations are idempotent. The longest opener wins; equally long openers
contribute all their closers. A matching closer wins a tie with an opener. Zero-width events are ignored.
Unmatched nesting can force scanning to EOF rather than exposing an internal terminator.

```csharp
parser.AddBracketPair(new Literal("("), new Literal(")"));
parser.AddBracketPair(new Literal("<"), new Literal(">"));
parser.AddRecoveryOpaqueTerminal(completeQuotedLiteral);
```

An opaque terminal must consume an entire string or other lexical region, including a defined malformed
extent. Do not register fragment terminals such as string text. Trivia already hides comments when the
language's trivia matcher consumes them. At depth zero, an opaque token may itself be a stop/anchor;
its interior remains hidden. The C# host registers complete character and string extents, selects the
string family by its opener, and observes host grammar replacements and rebuilt TDOPP rules.
Malformed C# regular strings fall back to the line boundary; malformed raw/verbatim families consume
to EOF conservatively. Custom lexical extensions should supply equally explicit malformed extents.

## Source and diagnostics

Inserted tokens have zero-width source spans. Absorbers preserve skipped source in `RawElements`;
list delimiters and trivia remain part of source coverage. A visitor that filters recovery nodes can
hide that text, so use raw children when checking coverage or retaining damaged input.

Tree-attached diagnostics use canonical positions. A missing token at a line start is displayed at the
end of the previous line; its syntax node keeps its original zero-width span. The public diagnostic
list and `DeriveRecoveryDiagnostics(root/subtree)` share the same attached diagnostic instances.
`Unrecovered` and `InsufficientStack` are result-level markers and are not subtree-derived.
Kinds are `Inserted`, `Skipped`, `Extraneous`, `Unrecovered`, and `InsufficientStack`.

## Budgets and optional policy

S0 is tried first. A bounded local repair can then absorb unknown input at the start of an actual
Ref-owned repeating item, stopping before that item's recognized FIRST token or a lexical/nesting
boundary. It scans at most 64 characters, consumes input, uses the S2 budget/mask, and is disabled by
explicit `Anchors`/`CanStart`. Set `EnableLocalGarbageRecovery = false` for differential checks.
This is context-local vocabulary, not a global lexer or a claim that skipped text was valid.

S1–S5 use the effective strategy mask and tier budgets. S0 and S6 remain exempt. The legacy
`MaxRecoveryAttemptsPerPosition` is not the active tier budget. `TimeBudget` causes cooperative
degradation, not a hard deadline: an individual matcher, probe, or bottom-tier scan can overrun it.

Clustering is off by default. To evaluate it on a representative corpus:

```csharp
parser.Profile = RecoveryProfile.Ide with
{
    EnableRecoveryClustering = true,
    ClusterDistance = 32,
    ClusterWorkThreshold = 16
};
```

The policy counts candidate work and probe misses at accepted skips, then uses coarse tiers nearby.
It resets across distance gaps or intact repeating items and defers at recognized item prefixes.
It does not suppress diagnostics or guarantee linear scaling. Do not enable it solely from EOF counts;
compare retained constructs, diagnostic spans, time distributions, and allocations.

Depth metrics expose initial/effective limits, observed host/probe depth, and guard firings. Available
thread stack can stop parsing below the absolute 400-frame cap. Rejected repair patches and snapshots
roll back; consumed depth/work budget remains session-wide to prevent retry loops. Use separate parser
instances for concurrent parses; sequential reuse resets session state.
