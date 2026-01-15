using ExtensibleParaser;

using global::CppEnumExtractor;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace EnumGenerator;

[Generator]
public class EnumGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var cppFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
                        || file.Path.EndsWith(".h", StringComparison.OrdinalIgnoreCase)
                        || file.Path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
            .Select((file, cancellationToken) => new CppFileInfo(
                    FullPath: file.Path,
                    Content: file.GetText(cancellationToken)?.ToString() ?? ""))
            .Collect();

        var allAdditionalFiles = context.AdditionalTextsProvider
            .Select((file, cancellationToken) => file.Path)
            .Collect();

        var compilationProvider = context.CompilationProvider;

        var combined = compilationProvider
            .Combine(cppFiles)
            .Combine(allAdditionalFiles);

        context.RegisterSourceOutput(combined, (spc, source) =>
            Execute(spc, source.Left.Left, source.Left.Right, source.Right));
    }

    private void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<CppFileInfo> cppFiles,
        ImmutableArray<string> allAdditionalFilePaths)
    {
        try
        {
            var attributeFiles = GetCppFilesFromAttributes(compilation, context, allAdditionalFilePaths, cppFiles);

            var allFiles = attributeFiles
                .Concat(cppFiles)
                .DistinctBy(f => f.FullPath)
                .Where(f => !string.IsNullOrEmpty(f.Content))
                .ToImmutableArray();

            if (allFiles.Length == 0)
                return;

            var allEnums = new List<EnumInfo>();
            var parser = new CppParser();

            //Debugger.Launch();

            foreach (var cppFile in allFiles)
            {
                try
                {
                    var parseResult = parser.Parse(cppFile.Content);

                    switch (parseResult)
                    {
                        case Success success:
                            CollectEnums(success.Program, new List<string>(), allEnums, cppFile.FullPath);
                            break;

                        case Failed failed:
                            var errorInfo = failed.ErrorInfo;
                            var location = CreateLocation(cppFile.FullPath, errorInfo.Location, allAdditionalFilePaths);

                            context.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "CPP001",
                                    "C++ parse error",
                                    errorInfo.GetErrorText(),
                                    nameof(EnumGenerator),
                                    DiagnosticSeverity.Error,
                                    true),
                                location ?? Location.None));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "CPP002",
                            "Generator error",
                            $"Error processing C++ file {GetFileName(cppFile.FullPath)}: {ex.Message}",
                            nameof(EnumGenerator),
                            DiagnosticSeverity.Error,
                            true),
                        Location.None));
                }
            }

            if (allEnums.Count == 0)
                return;

            var sourceCode = GenerateCSharpCode(allEnums);
            context.AddSource("GeneratedEnums.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "CPP000",
                    "Generator fatal error",
                    $"Generator fatal error: {ex.Message}",
                    nameof(EnumGenerator),
                    DiagnosticSeverity.Error,
                    true),
                Location.None));
        }
    }

    private ImmutableArray<CppFileInfo> GetCppFilesFromAttributes(
        Compilation compilation,
        SourceProductionContext context,
        ImmutableArray<string> allAdditionalFilePaths,
        ImmutableArray<CppFileInfo> cppFiles)
    {
        var results = new List<CppFileInfo>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var attributeSyntaxes = root.DescendantNodes()
                .OfType<AttributeSyntax>()
                .Where(attr => attr.Name.ToString() is "CppEnumSource" or "CppEnumSourceAttribute");

            foreach (var attributeSyntax in attributeSyntaxes)
            {
                if (attributeSyntax.ArgumentList?.Arguments.Count > 0)
                {
                    var arg = attributeSyntax.ArgumentList.Arguments[0];
                    if (arg.Expression is LiteralExpressionSyntax literal && literal.Token.Value is string filePath)
                    {
                        var attributeLocation = attributeSyntax.GetLocation();

                        var (foundPath, content) = FindCppFile(filePath, allAdditionalFilePaths, cppFiles);

                        if (content != null && foundPath != null)
                            results.Add(new CppFileInfo(foundPath, content));
                        else
                            context.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "CPP005",
                                    "C++ file not found",
                                    $"C++ file not found: {filePath}. File must be added as AdditionalFiles in .csproj",
                                    nameof(EnumGenerator),
                                    DiagnosticSeverity.Error,
                                    true),
                                attributeLocation));
                    }
                }
            }
        }

        return results.ToImmutableArray();
    }

    private (string? FoundPath, string? Content) FindCppFile(
        string filePath,
        ImmutableArray<string> allAdditionalFilePaths,
        ImmutableArray<CppFileInfo> cppFiles)
    {
        foreach (var cppFile in cppFiles)
        {
            if (string.Equals(cppFile.FullPath, filePath, StringComparison.OrdinalIgnoreCase))
                return (cppFile.FullPath, cppFile.Content);

            var fileName = Path.GetFileName(cppFile.FullPath);
            var targetFileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, targetFileName, StringComparison.OrdinalIgnoreCase))
                return (cppFile.FullPath, cppFile.Content);
        }

        foreach (var additionalFilePath in allAdditionalFilePaths)
        {
            if (string.Equals(additionalFilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return (additionalFilePath, null);

            var fileName = Path.GetFileName(additionalFilePath);
            var targetFileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, targetFileName, StringComparison.OrdinalIgnoreCase))
                return (additionalFilePath, null);

            var fullTargetPath = Path.GetFullPath(filePath);
            var fullAdditionalPath = Path.GetFullPath(additionalFilePath);
            if (string.Equals(fullTargetPath, fullAdditionalPath, StringComparison.OrdinalIgnoreCase))
                return (additionalFilePath, null);
        }

        return (null, null);
    }

    private void CollectEnums(CppAst node, List<string> namespaceParts, List<EnumInfo> result, string sourceFile)
    {
        switch (node)
        {
            case CppProgram program:
                foreach (var item in program.Items)
                    CollectEnums(item, namespaceParts, result, sourceFile);
                break;

            case NamespaceDeclaration ns:
                var nsName = ns.Name;
                var newNamespaceParts = new List<string>(namespaceParts) { nsName };
                CollectEnums(ns.Body, newNamespaceParts, result, sourceFile);
                break;

            case AnonymousNamespaceDeclaration anonymousNs:
                CollectEnums(anonymousNs.Body, namespaceParts, result, sourceFile);
                break;

            case EnumDeclaration enumDecl:
                ProcessEnumDeclaration(enumDecl, namespaceParts, result, sourceFile);
                break;
        }
    }

    private void ProcessEnumDeclaration(EnumDeclaration enumDecl, List<string> namespaceParts,
        List<EnumInfo> result, string sourceFile)
    {
        var originalNamespace = string.Join("::", namespaceParts);
        var enumName = enumDecl.Name;
        var cppFullName = string.IsNullOrEmpty(originalNamespace)
            ? enumName
            : $"{originalNamespace}::{enumName}";

        // Определяем имя для C#
        var csharpName = enumName;
        var shouldRemoveLastNameFromNamespace = false;

        if (csharpName == "Enum" && namespaceParts.Count >= 1)
        {
            csharpName = namespaceParts[namespaceParts.Count - 1];
            shouldRemoveLastNameFromNamespace = true;
        }

        csharpName = ConvertToPascalCase(csharpName);

        // Формируем C# пространство имен
        var csharpNamespaceParts = new List<string>();
        for (int i = 0; i < namespaceParts.Count; i++)
        {
            if (shouldRemoveLastNameFromNamespace && i == namespaceParts.Count - 1)
                continue;

            csharpNamespaceParts.Add(ConvertToPascalCase(namespaceParts[i]));
        }

        var csharpNamespace = csharpNamespaceParts.Count > 0
            ? string.Join(".", csharpNamespaceParts)
            : ConvertToPascalCase(Path.GetFileNameWithoutExtension(sourceFile));

        result.Add(new EnumInfo(
            Name: csharpName,
            OriginalName: enumName,
            OriginalNamespace: originalNamespace,
            Members: enumDecl.Members,
            Namespace: csharpNamespace,
            SourceFile: sourceFile,
            IsEnumClass: enumDecl.IsClass,
            UnderlyingType: enumDecl.UnderlyingType
        ));
    }

    private string GetFileName(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return "unknown";

        try
        {
            return Path.GetFileName(fullPath) ?? "unknown";
        }
        catch
        {
            return fullPath;
        }
    }

    private static string ConvertToPascalCase(string cppName)
    {
        if (string.IsNullOrEmpty(cppName))
            return cppName;

        if (!cppName.Contains("_", StringComparison.Ordinal) && cppName.Length > 0 && char.IsUpper(cppName[0]))
            return cppName.EndsWith(";", StringComparison.Ordinal) ? cppName[..^1] : cppName;

        var length = cppName.Length;
        if (cppName.EndsWith(";", StringComparison.Ordinal))
            length--;

        var result = new StringBuilder(length);
        var makeUpper = true;

        for (int i = 0; i < length; i++)
        {
            var c = cppName[i];

            if (c == '_')
            {
                makeUpper = true;
                continue;
            }

            if (makeUpper)
            {
                result.Append(char.ToUpperInvariant(c));
                makeUpper = false;
            }
            else
                result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }

    private string GenerateCSharpCode(List<EnumInfo> enums)
    {
        var sb = new StringBuilder();

        sb.AppendLine("""
            // <auto-generated />
            // Generated from C++ enums
            #pragma warning disable

            """);

        var namespaceGroups = enums.GroupBy(e => e.Namespace).OrderBy(g => g.Key);

        foreach (var group in namespaceGroups)
        {
            var namespaceName = string.IsNullOrEmpty(group.Key) ? "GeneratedEnums" : group.Key;

            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");

            foreach (var enumInfo in group.OrderBy(e => e.Name))
            {
                bool hasBitwiseOperations = enumInfo.Members.Any(m =>
                    m.Value != null && (m.Value.Contains("<<") || m.Value.Contains("|")));

                if (hasBitwiseOperations)
                    sb.AppendLine("    [System.Flags]");

                // Полный путь к файлу с нормализацией разделителей
                var normalizedPath = enumInfo.SourceFile?.Replace('\\', '/') ?? "";
                var fileName = GetFileName(normalizedPath);

                var typeInfo = enumInfo.IsEnumClass ? "enum class" : "enum";
                var typeSuffix = enumInfo.UnderlyingType != null ? $" : {enumInfo.UnderlyingType}" : "";
                var csTypeSuffix = enumInfo.UnderlyingType switch
                {
                    "int8_t"   => "sbyte",
                    "int16_t"  => "short",
                    "int32_t"  => null,  // default
                    "int64_t"  => "long",
                    "uint8_t"  => "byte",
                    "uint16_t" => "ushort",
                    "uint32_t" => "uint",
                    "uint64_t" => "ulong",
                    null => null,
                    var x => x
                };

                sb.AppendLine($$"""
                        /// <summary>
                        /// Generated from C++ {{typeInfo}} '{{enumInfo.OriginalName}}{{typeSuffix}}'
                        /// C++ namespace: {{(string.IsNullOrEmpty(enumInfo.OriginalNamespace) ? "(global)" : enumInfo.OriginalNamespace)}}
                        /// Source file: {{fileName}} ({{normalizedPath}})
                        /// </summary>
                        public enum {{enumInfo.Name}}
                        {
                    """);

                for (int i = 0; i < enumInfo.Members.Count; i++)
                {
                    var member = enumInfo.Members[i];
                    var memberName = ConvertMemberName(member.Name);

                    if (!string.IsNullOrEmpty(member.Value))
                        sb.AppendLine($"        {memberName} = {member.Value},");
                    else
                        sb.AppendLine($"        {memberName},");
                }

                sb.AppendLine("    }");

                if (group.Last() != enumInfo)
                    sb.AppendLine();
            }

            sb.AppendLine("}");

            if (namespaceGroups.Last() != group)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    private string ConvertMemberName(string memberName)
    {
        if (string.IsNullOrEmpty(memberName))
            return memberName;

        if (char.IsUpper(memberName[0]) || IsAllUpperCase(memberName))
            return memberName;

        var parts = memberName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return memberName;

        return string.Concat(parts.Select(part =>
        {
            if (string.IsNullOrEmpty(part))
                return part;

            if (part.Length == 1)
                return part.ToUpperInvariant();

            return char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
        }));
    }

    private bool IsAllUpperCase(string str)
    {
        return !string.IsNullOrEmpty(str) && str.All(c => char.IsUpper(c) || char.IsDigit(c));
    }

    private Location? CreateLocation(string filePath, (int Line, int Col) location, ImmutableArray<string> allAdditionalFilePaths)
    {
        try
        {
            var foundPath = allAdditionalFilePaths.FirstOrDefault(path =>
                string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(foundPath))
            {
                var fileName = Path.GetFileName(filePath);
                foundPath = allAdditionalFilePaths.FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
            }

            if (foundPath is null)
                return null;

            var linePosition = new LinePosition(location.Line - 1, location.Col - 1);

            return Location.Create(foundPath, new TextSpan(0, 0), new LinePositionSpan(linePosition, linePosition));
        }
        catch
        {
            return null;
        }
    }

    private record CppFileInfo(string FullPath, string Content);

    private record EnumInfo(
        string Name,
        string OriginalName,
        string OriginalNamespace,
        List<EnumMember> Members,
        string Namespace,
        string SourceFile,
        bool IsEnumClass = false,
        string? UnderlyingType = null);
}
