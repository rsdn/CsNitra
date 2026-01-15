using EnumGeneratorTest;
using ProductPlatform.SupportTools;
using ReportProgressCallback;

[assembly: CppEnumSource(@"types.h")]
[assembly: CppEnumSource(@"loader.cpp")]

namespace EnumGeneratorTest;

internal class Program
{
    static void Main(string[] args)
    {
        var errorScenario = ErrorScenario.CrashOrFreeze;
        Console.WriteLine($"errorScenario = {errorScenario}; ToolExitCodes = {Loader.ToolExitCodes.WindowsShutdown}; ReportStopReason={ReportStopReason.ArchiveTooBig}");
    }
}
