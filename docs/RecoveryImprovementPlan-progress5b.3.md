# Progress — 5b.3.1 (A5-5 SoftSeparator)

## What was added

A `SoftSeparator` option to the `SeparatedList` rule type (`ExtensibleParser/Rules.cs`).

Exact declaration (primary-constructor record parameter, default `null`):

```csharp
public record SeparatedList(
    Rule Element,
    Rule Separator,
    string Kind,
    SeparatorEndBehavior EndBehavior = SeparatorEndBehavior.Optional,
    bool CanBeEmpty = true,
    Terminal? SoftSeparator = null)
    : Rule(Kind)
```

Plus an XML doc comment for the new parameter, matching the existing parameter docs.

## Pattern followed

`SeparatedList` is a `record` with a **primary constructor**; its existing optional
settings (`EndBehavior`, `CanBeEmpty`) are declared as **constructor parameters with
default values** — there is no separate static factory method or property block for
them (the task mentioned a factory-method argument; none exists on this type, so the
mirrored pattern is constructor-parameter-only, which also acts as the property via
the record's positional parameters).

`SoftSeparator` was appended after `CanBeEmpty` with default `null`, mirroring
`SeparatorEndBehavior EndBehavior = SeparatorEndBehavior.Optional` /
`bool CanBeEmpty = true`.

Additionally, `InlineReferences` carries the new option through, exactly as it
already does for `EndBehavior` and `CanBeEmpty`:

```csharp
return new SeparatedList(inlinedElement, inlinedSeparator, Kind, EndBehavior, CanBeEmpty, SoftSeparator);
```

## Scope

- Declaration only. No parsing behavior (that is 5b.3.2). `SoftSeparator` is not
  read by any parser/recovery code yet; default `null` changes no existing behavior.
- Only `ExtensibleParser/Rules.cs` was modified.

## Build result

`dotnet build` from repo root: **succeeded, 0 errors** (Debug, .NET 8, SDK 8.0.100 pin,
`--no-incremental` check also green).
