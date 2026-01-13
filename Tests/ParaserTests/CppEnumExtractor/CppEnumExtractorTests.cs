using ExtensibleParaser;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Diagnostics;

namespace CppEnumExtractor.Tests;

[TestClass]
public class CppParserTests
{
    private readonly CppParser _parser = new CppParser();

    [TestMethod]
    public void Parse_SimpleEnum_ReturnsEnumDeclaration()
    {
        TestCpp(
            "Program",
            """
            enum Colors {
                Red,
                Green,
                Blue
            }
            """,
            "enum Colors {\nRed,\nGreen,\nBlue\n}"
        );
    }

    [TestMethod]
    public void Parse_EnumWithValues_ReturnsEnumWithExpressions()
    {
        TestCpp(
            "Program",
            """
            enum Flags {
                None = 0,
                Read = 1 << 0,
                Write = 1 << 1,
                Execute = 1 << 2,
                All = Read | Write | Execute
            }
            """,
            "enum Flags {\nNone = 0,\nRead = 1 << 0,\nWrite = 1 << 1,\nExecute = 1 << 2,\nAll = Read | Write | Execute\n}"
        );
    }

    [TestMethod]
    public void Parse_EnumWithComplexExpressions_ReturnsCorrectAst()
    {
        TestCpp(
            "Program",
            """
            enum Values {
                First = 1,
                Second = 2 * 3,
                Third = (1 + 2) << 4,
                Fourth = 0xFF,
                Fifth = 0b1010
            }
            """,
            "enum Values {\nFirst = 1,\nSecond = 2 * 3,\nThird = (1 + 2) << 4,\nFourth = 0xFF,\nFifth = 0b1010\n}"
        );
    }

    [TestMethod]
    public void Parse_NamespaceWithEnum_ReturnsNestedStructure()
    {
        TestCpp(
            "Program",
            """
            namespace Outer {
                enum Colors {
                    Red,
                    Green,
                    Blue
                }
            }
            """,
            "namespace Outer {\n" +
            "enum Colors {\nRed,\nGreen,\nBlue\n}\n" +
            "}"
        );
    }

    [TestMethod]
    public void Parse_NestedNamespaces_ReturnsDeeplyNestedStructure()
    {
        TestCpp(
            "Program",
            """
            namespace A
            {
                namespace B {
                    namespace C
                    {
                        enum Status
                        {
                            Ok,
                            Error
                        }
                    }
                }
            }
            """,
            "namespace A {\n" +
            "namespace B {\n" +
            "namespace C {\n" +
            "enum Status {\nOk,\nError\n}\n" +
            "}\n" +
            "}\n" +
            "}"
        );
    }

    [TestMethod]
    public void Parse_MultipleEnums_ReturnsAllEnums()
    {
        TestCpp(
            "Program",
            """
            namespace Types {
                enum Colors {
                    Red,
                    Green
                }

                enum Status {
                    Active,
                    Inactive
                }
            }
            """,
            "namespace Types {\n" +
            "enum Colors {\nRed,\nGreen\n}\n" +
            "enum Status {\nActive,\nInactive\n}\n" +
            "}"
        );
    }

    [TestMethod]
    public void Parse_SkipsNonEnumLines_IgnoresOtherCode()
    {
        TestCpp(
            "Program",
            """
            namespace Example {
                int x = 5;  // Эта строка должна быть проигнорирована
                float y = 3.14f;  // И эта тоже
                
                enum Colors {
                    Red,
                    Green
                }
                
                void func() { }  // И эта тоже
            }
            """,
            "namespace Example {\n" +
            "enum Colors {\nRed,\nGreen\n}\n" +
            "}"
        );
    }

    [TestMethod]
    public void Parse_EmptyEnum_ReturnsEmptyEnum()
    {
        TestCpp(
            "Program",
            """
            enum Empty {
            }
            """,
            "enum Empty {\n\n}"
        );
    }

    [TestMethod]
    public void Parse_EnumWithTrailingComma_HandlesTrailingSeparator()
    {
        TestCpp(
            "Program",
            """
            enum Colors {
                Red,
                Green,
                Blue,
            }
            """,
            "enum Colors {\nRed,\nGreen,\nBlue\n}"
        );
    }

    [TestMethod]
    public void Parse_EnumWithSingleMember_ReturnsSingleMember()
    {
        TestCpp(
            "Program",
            """
            enum Single {
                OnlyValue = 42
            }
            """,
            "enum Single {\nOnlyValue = 42\n}"
        );
    }

    [TestMethod]
    public void Parse_MultipleClosingBraces_HandlesMultipleBraces()
    {
        TestCpp(
            "Program",
            """
            namespace Outer {
                namespace Inner {
                    enum Test {
                        A,
                        B
                    }
                }
            }
            """,
            """
            namespace Outer {
            namespace Inner {
            enum Test {
            A,
            B
            }
            }
            }
            """
        );
    }

    [TestMethod]
    public void Parse_WithComments_IgnoresComments()
    {
        TestCpp(
            "Program",
            """
            // Комментарий в начале файла
            namespace Types {
                // Комментарий перед enum
                enum Colors {
                    Red,    // Комментарий после значения
                    Green   // Другой комментарий
                    // Комментарий в середине
                }
                // Комментарий после enum
            }
            """,
            "namespace Types {\n" +
            "enum Colors {\nRed,\nGreen\n}\n" +
            "}"
        );
    }

    [TestMethod]
    public void Parse_MixedCaseIdentifiers_HandlesCaseSensitivity()
    {
        TestCpp(
            "Program",
            """
            enum MixedCase {
                camelCaseValue,
                PascalCaseValue,
                snake_case_value,
                UPPER_CASE_VALUE = 100,
                mixed_Case_Value123
            }
            """,
            "enum MixedCase {\n" +
            "camelCaseValue,\n" +
            "PascalCaseValue,\n" +
            "snake_case_value,\n" +
            "UPPER_CASE_VALUE = 100,\n" +
            "mixed_Case_Value123\n" +
            "}"
        );
    }

    [TestMethod]
    public void Parse_MultipleTopLevelEnums_ReturnsAllEnums()
    {
        TestCpp(
            "Program",
            """
            enum First {
                A,
                B
            }

            enum Second {
                X = 1,
                Y = 2
            }
            """,
            "enum First {\nA,\nB\n}\n" +
            "enum Second {\nX = 1,\nY = 2\n}"
        );
    }

    [TestMethod]
    public void Parse_ComplexRealWorldExample_HandlesComplexCase()
    {
        TestCpp(
            "Program",
            """
            namespace Graphics {
                namespace Rendering {
                    enum BufferUsage {
                        Static = 0,
                        Dynamic = 1 << 0,
                        Stream = 1 << 1,
                        Read = 1 << 2,
                        Write = 1 << 3,
                        ReadWrite = Read | Write
                    }

                    enum PrimitiveType {
                        Points,
                        Lines,
                        Triangles,
                        TriangleStrip
                    }
                }

                namespace UI {
                    enum Alignment {
                        Left = 0,
                        Center = 1,
                        Right = 2,
                        Top = 1 << 2,
                        Bottom = 1 << 3,
                        VerticalCenter = 1 << 4
                    }
                }
            }
            """,
            "namespace Graphics {\n" +
            "namespace Rendering {\n" +
            "enum BufferUsage {\n" +
            "Static = 0,\n" +
            "Dynamic = 1 << 0,\n" +
            "Stream = 1 << 1,\n" +
            "Read = 1 << 2,\n" +
            "Write = 1 << 3,\n" +
            "ReadWrite = Read | Write\n" +
            "}\n" +
            "enum PrimitiveType {\n" +
            "Points,\n" +
            "Lines,\n" +
            "Triangles,\n" +
            "TriangleStrip\n" +
            "}\n" +
            "}\n" +
            "namespace UI {\n" +
            "enum Alignment {\n" +
            "Left = 0,\n" +
            "Center = 1,\n" +
            "Right = 2,\n" +
            "Top = 1 << 2,\n" +
            "Bottom = 1 << 3,\n" +
            "VerticalCenter = 1 << 4\n" +
            "}\n" +
            "}\n" +
            "}"
        );
    }

    private void TestCpp(string startRule, string input, string expectedAst)
    {
        Trace.WriteLine($"\n=== TEST START: {input} ===");

        var parseResult = _parser.Parse(input);

        Trace.WriteLine($"Parsed AST:\n{parseResult}");
        Trace.WriteLine($"Expected AST:\n{expectedAst}");

        Assert.AreEqual(expectedAst.NormalizeEol(), parseResult.ToString().NormalizeEol());
    }

    [TestMethod]
    public void Parse_TypesHFile_IgnoresStructsAndMacros()
    {
        // Arrange
        var input = """
            namespace test {
                struct ShouldBeIgnored {
                    int field;
                };
                
                enum ValidEnum {
                    Value1,
                    Value2 = 42
                };
                
                #define MACRO_SHOULD_BE_IGNORED 1
                
                void function() {}
            }
            """;

        // Act
        var parseResult = _parser.Parse(input);
        var resultString = parseResult.ToString();

        // Assert - должен быть извлечен только enum, struct и макросы должны быть проигнорированы
        var expected = """
            namespace test {
            enum ValidEnum {
            Value1,
            Value2 = 42
            }
            }
            """;

        Assert.AreEqual(expected.NormalizeEol(), resultString.NormalizeEol());
    }

    [TestMethod]
    public void Parse_TypesHFile_HandlesMultipleEnumInSameNamespace()
    {
        // Arrange
        var input = """
            namespace same_namespace {
                enum FirstEnum {
                    A,
                    B
                };
                
                enum SecondEnum {
                    X = 1,
                    Y = 2
                };
            }
            """;

        // Act
        var parseResult = _parser.Parse(input);

        // Assert
        var sameNamespace = (NamespaceDeclaration)parseResult.Items[0];
        Assert.AreEqual(2, sameNamespace.Body.Items.Count);

        var firstEnum = (EnumDeclaration)sameNamespace.Body.Items[0];
        var secondEnum = (EnumDeclaration)sameNamespace.Body.Items[1];

        Assert.AreEqual("FirstEnum", firstEnum.Name);
        Assert.AreEqual(2, firstEnum.Members.Count);

        Assert.AreEqual("SecondEnum", secondEnum.Name);
        Assert.AreEqual(2, secondEnum.Members.Count);
        Assert.AreEqual("1", secondEnum.Members[0].Value);
        Assert.AreEqual("2", secondEnum.Members[1].Value);
    }


    [TestMethod]
    public void Parse_TypesHFile_ExtractsAllEnums()
    {
        // Arrange
        var input = """
            #ifndef PRODUCT_PLATFORM_IFACE_SUPPORT_TOOLS_TYPES_H
            #define PRODUCT_PLATFORM_IFACE_SUPPORT_TOOLS_TYPES_H

            #include <component/xxx/rtl/types.h>
            #include <component/xxx/rtl/string.h>
            #include <component/xxx/rtl/enum_value.h>

            namespace product_platform
            {

            namespace support_tools
            {

            namespace error_scenario
            {

            enum Enum
            {
                CrashOrFreeze,
                WebPageFailure,
                ActivationFailure,
                Other
            };
            typedef xxx::enum_value_t<Enum> Type;

            } // namespace error_scenario

            namespace troubleshooter_state
            {

            enum Enum
            {
                TroubleshooterNotRunning,
                TroubleshooterRunning,
                SessionInProgress,
            };
            typedef xxx::enum_value_t<Enum> Type;

            } // namespace troubleshooter_state

            namespace troubleshooter_other_instances_state
            {

            enum Enum
            {
                NoOtherInstances,
                AnotherInstanceLaunchPending,
                AnotherInstanceRunning,
            };
            typedef xxx::enum_value_t<Enum> Type;

            }

            /// <summary>
            /// Support tools session configuration
            /// </summary>
            /// @serializable
            struct SessionConfig
            {
                EKA_DECLARE_SERID(0xq2321243);

                bool operator==(const SessionConfig& other) const noexcept
                {
                    return errorScenario == other.errorScenario && 
                        helpUrlTemplate == other.helpUrlTemplate &&
                        recordingEnabled == other.recordingEnabled && 
                        lowLevelTracesEnabled == other.lowLevelTracesEnabled &&
                        useGdiForRecording == other.useGdiForRecording &&
                        runGsiAfterFinish == other.runGsiAfterFinish;
                };
                
                error_scenario::Type errorScenario{ error_scenario::Other };
                xxx::string16_t helpUrlTemplate;
                
                xxx::bool_t recordingEnabled{true};
                xxx::bool_t lowLevelTracesEnabled{false};
                xxx::bool_t useGdiForRecording{false};
                xxx::bool_t runGsiAfterFinish{false};
            };

            /// <summary>
            /// Tool startup info used in tool utility
            /// </summary>
            /// @serializable
            struct StartupInfo
            {
                EKA_DECLARE_SERID(0xbe244545);

                xxx::string16_t onlineHelpUrl; // url for online help in tool utility
                troubleshooter_other_instances_state::Type otherInstancesState; // information about other running instances
                datetime_t sessionStartTime; // session start time in UTC 
                                            //(for continued recording after reboot will be earlier than current application launch)
            };

            }

            }

            #endif // PRODUCT_PLATFORM_IFACE_SUPPORT_TOOLS_TYPES_H
            """;

        // Act
        var parseResult = _parser.Parse(input);
        var resultString = parseResult.ToString();

        // Debug output
        Trace.WriteLine($"\n=== Parsed AST ===");
        Trace.WriteLine(resultString);
        Trace.WriteLine($"\n=== Expected AST ===");
        var expectedAst = """
            namespace product_platform {
            namespace support_tools {
            namespace error_scenario {
            enum Enum {
            CrashOrFreeze,
            WebPageFailure,
            ActivationFailure,
            Other
            }
            }
            namespace troubleshooter_state {
            enum Enum {
            TroubleshooterNotRunning,
            TroubleshooterRunning,
            SessionInProgress
            }
            }
            namespace troubleshooter_other_instances_state {
            enum Enum {
            NoOtherInstances,
            AnotherInstanceLaunchPending,
            AnotherInstanceRunning
            }
            }
            }
            }
            """;

        Trace.WriteLine(expectedAst);

        // Assert
        Assert.AreEqual(expectedAst.NormalizeEol(), resultString.NormalizeEol());

        // Дополнительные проверки
        var productPlatform = (NamespaceDeclaration)parseResult.Items[0];
        Assert.AreEqual("product_platform", productPlatform.Name);

        var supportTools = (NamespaceDeclaration)productPlatform.Body.Items[0];
        Assert.AreEqual("support_tools", supportTools.Name);

        // Проверяем, что извлеклись все три enum
        Assert.AreEqual(3, supportTools.Body.Items.Count);

        var errorScenario = (NamespaceDeclaration)supportTools.Body.Items[0];
        var troubleshooterState = (NamespaceDeclaration)supportTools.Body.Items[1];
        var troubleshooterOtherInstancesState = (NamespaceDeclaration)supportTools.Body.Items[2];

        Assert.AreEqual("error_scenario", errorScenario.Name);
        Assert.AreEqual("troubleshooter_state", troubleshooterState.Name);
        Assert.AreEqual("troubleshooter_other_instances_state", troubleshooterOtherInstancesState.Name);

        // Проверяем содержимое первого enum
        var errorScenarioEnum = (EnumDeclaration)errorScenario.Body.Items[0];
        Assert.AreEqual("Enum", errorScenarioEnum.Name);
        Assert.AreEqual(4, errorScenarioEnum.Members.Count);
        Assert.AreEqual("CrashOrFreeze", errorScenarioEnum.Members[0].Name);
        Assert.AreEqual("WebPageFailure", errorScenarioEnum.Members[1].Name);
        Assert.AreEqual("ActivationFailure", errorScenarioEnum.Members[2].Name);
        Assert.AreEqual("Other", errorScenarioEnum.Members[3].Name);

        // Проверяем содержимое второго enum
        var troubleshooterStateEnum = (EnumDeclaration)troubleshooterState.Body.Items[0];
        Assert.AreEqual("Enum", troubleshooterStateEnum.Name);
        Assert.AreEqual(3, troubleshooterStateEnum.Members.Count);
        Assert.AreEqual("TroubleshooterNotRunning", troubleshooterStateEnum.Members[0].Name);
        Assert.AreEqual("TroubleshooterRunning", troubleshooterStateEnum.Members[1].Name);
        Assert.AreEqual("SessionInProgress", troubleshooterStateEnum.Members[2].Name);

        // Проверяем содержимое третьего enum
        var troubleshooterOtherInstancesStateEnum = (EnumDeclaration)troubleshooterOtherInstancesState.Body.Items[0];
        Assert.AreEqual("Enum", troubleshooterOtherInstancesStateEnum.Name);
        Assert.AreEqual(3, troubleshooterOtherInstancesStateEnum.Members.Count);
        Assert.AreEqual("NoOtherInstances", troubleshooterOtherInstancesStateEnum.Members[0].Name);
        Assert.AreEqual("AnotherInstanceLaunchPending", troubleshooterOtherInstancesStateEnum.Members[1].Name);
        Assert.AreEqual("AnotherInstanceRunning", troubleshooterOtherInstancesStateEnum.Members[2].Name);
    }
}

// Метод расширения для нормализации переводов строк (из MiniCTests)
public static class TestExtensions
{
    public static string NormalizeEol(this string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
