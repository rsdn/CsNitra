using EnumGeneratorTest;
using ProductPlatform.SupportTools;

[assembly: CppEnumSource(@"types.h")]
[assembly: CppEnumSource(@"loader.cpp")]

namespace EnumGeneratorTest;

internal class Program
{
    static void Main(string[] args)
    {
        var errorScenario = ErrorScenario.CrashOrFreeze;
        Console.WriteLine($"errorScenario = {errorScenario}; ToolExitCodes = {Loader.ToolExitCodes.WindowsShutdown}");
    }
}
