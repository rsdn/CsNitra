using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public class PreprocessorSmokeTests
{
    [TestMethod]
    public void Run_PassthroughSource_ReturnsSourceUnchanged()
    {
        var result = Preprocessor.Run("int x = 1;", Array.Empty<string>());

        Assert.AreEqual("int x = 1;", result.Text);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual(0, result.LineDirectives.Count);
    }
}
