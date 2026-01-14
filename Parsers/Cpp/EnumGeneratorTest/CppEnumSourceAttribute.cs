namespace EnumGeneratorTest;

/// <summary>
/// Указывает путь к C++ файлу для извлечения enum
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class CppEnumSourceAttribute(string filePath) : Attribute
{
    /// <summary>
    /// Относительный путь к файлу C++ (относительно корня проекта)
    /// </summary>
    public string FilePath { get; } = filePath;
}
