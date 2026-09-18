using System.Collections.Generic;

namespace CsPreprocessor;

public sealed record PreprocessResult(string Text, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<LineDirective> LineDirectives);
