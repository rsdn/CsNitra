using System;
using System.Collections.Generic;

namespace CsPreprocessor;

public sealed class Preprocessor
{
    public static PreprocessResult Run(string source, IEnumerable<string> commandLineSymbols)
        => new(source, Array.Empty<Diagnostic>(), Array.Empty<LineDirective>());
}
