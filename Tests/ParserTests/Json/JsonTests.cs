using Json;

using System.Globalization;

namespace Json;

[TestClass]
public class JsonParserTests
{
    private readonly JsonParser _parser = new();

    [TestMethod]
    public void Parse_SimpleString_ReturnsJsonString()
    {
        var input = "\"hello\"";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonString));
        var jsonString = (JsonString)result;
        Assert.AreEqual("hello", jsonString.Value);
        Assert.AreEqual(0, jsonString.StartPos);
        Assert.AreEqual(7, jsonString.EndPos);
    }

    [TestMethod]
    public void Parse_Number_ReturnsJsonNumber()
    {
        var input = "42.5";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonNumber));
        var jsonNumber = (JsonNumber)result;
        Assert.AreEqual(42.5, jsonNumber.Value);
        Assert.AreEqual(0, jsonNumber.StartPos);
        Assert.AreEqual(4, jsonNumber.EndPos);
    }

    [TestMethod]
    public void Parse_Boolean_ReturnsJsonBoolean()
    {
        var input = "true";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonBoolean));
        Assert.IsTrue(((JsonBoolean)result).Value);
        Assert.AreEqual(0, result.StartPos);
        Assert.AreEqual(4, result.EndPos);
    }

    [TestMethod]
    public void Parse_Null_ReturnsJsonNull()
    {
        var input = "null";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonNull));
        Assert.AreEqual(0, result.StartPos);
        Assert.AreEqual(4, result.EndPos);
    }

    [TestMethod]
    public void Parse_EmptyObject_ReturnsEmptyJsonObject()
    {
        var input = "{}";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonObject));
        var jsonObject = (JsonObject)result;
        Assert.AreEqual(0, jsonObject.Properties.Count);
        Assert.AreEqual(0, jsonObject.StartPos);
        Assert.AreEqual(2, jsonObject.EndPos);
    }

    [TestMethod]
    public void Parse_ObjectWithProperties_ReturnsJsonObject()
    {
        var input = "{\"name\": \"John\", \"age\": 30}";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonObject));
        var jsonObject = (JsonObject)result;
        Assert.AreEqual(2, jsonObject.Properties.Count);
        Assert.AreEqual("name", jsonObject.Properties[0].Name);
        Assert.AreEqual("John", ((JsonString)jsonObject.Properties[0].Value).Value);
        Assert.AreEqual(0, jsonObject.StartPos);
    }

    [TestMethod]
    public void Parse_EmptyArray_ReturnsEmptyJsonArray()
    {
        var input = "[]";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonArray));
        Assert.AreEqual(0, ((JsonArray)result).Elements.Count);
        Assert.AreEqual(0, result.StartPos);
        Assert.AreEqual(2, result.EndPos);
    }

    [TestMethod]
    public void Parse_ArrayWithElements_ReturnsJsonArray()
    {
        var input = "[1, \"two\", true]";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonArray));
        var jsonArray = (JsonArray)result;
        Assert.AreEqual(3, jsonArray.Elements.Count);
        Assert.IsInstanceOfType(jsonArray.Elements[0], typeof(JsonNumber));
        Assert.IsInstanceOfType(jsonArray.Elements[1], typeof(JsonString));
        Assert.IsInstanceOfType(jsonArray.Elements[2], typeof(JsonBoolean));
        Assert.AreEqual(0, jsonArray.StartPos);
    }

    [TestMethod]
    public void Parse_NestedStructures_ReturnsCorrectAst()
    {
        var input = "{\"users\": [{\"name\": \"Alice\", \"active\": true}, {\"name\": \"Bob\", \"active\": false}]}";

        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonObject));
        var jsonObject = (JsonObject)result;
        Assert.AreEqual(1, jsonObject.Properties.Count);
        Assert.AreEqual("users", jsonObject.Properties[0].Name);

        var usersArray = (JsonArray)jsonObject.Properties[0].Value;
        Assert.AreEqual(2, usersArray.Elements.Count);
    }

    [TestMethod]
    public void Parse_StringWithEscapes_HandlesEscapesCorrectly()
    {
        var input = "\"line1\\nline2\\ttab\"";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonString));
        var jsonString = (JsonString)result;
        Assert.AreEqual("line1\nline2\ttab", jsonString.Value);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void Parse_InvalidJson_ThrowsException()
    {
        var input = "{ invalid: json }";
        _ = _parser.Parse(input);
    }

    [TestMethod]
    public void Parse_WithWhitespace_IgnoresWhitespace()
    {
        var input = "  {  \n  \"key\"  :  \t  \"value\"  \r\n  }  ";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonObject));
        var jsonObject = (JsonObject)result;
        Assert.AreEqual(1, jsonObject.Properties.Count);
        Assert.AreEqual("key", jsonObject.Properties[0].Name);
        Assert.AreEqual("value", ((JsonString)jsonObject.Properties[0].Value).Value);
    }

    [TestMethod]
    public void Parse_UnicodeEscape_HandlesUnicode()
    {
        var input = "\"\\u03A9\"";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonString));
        var jsonString = (JsonString)result;
        Assert.AreEqual("Ω", jsonString.Value);
    }

    [TestMethod]
    public void Parse_ComplexNestedStructure_ReturnsCorrectAst()
    {
        var input = """
        {
            "users": [
                {
                    "name": "Alice",
                    "age": 25,
                    "active": true,
                    "tags": ["admin", "user"],
                    "metadata": {
                        "created": "2023-01-01",
                        "modified": "2023-12-01"
                    }
                },
                {
                    "name": "Bob",
                    "age": 30,
                    "active": false,
                    "tags": ["user"],
                    "metadata": {
                        "created": "2023-02-01",
                        "modified": null
                    }
                }
            ],
            "count": 2,
            "enabled": true
        }
        """;

        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonObject));
        var jsonObject = (JsonObject)result;
        Assert.AreEqual(3, jsonObject.Properties.Count);
    }

    [TestMethod]
    public void Parse_ArrayWithDifferentTypes_ReturnsCorrectAst()
    {
        var input = """[1, "string", true, null, {"key": "value"}]""";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonArray));
        var jsonArray = (JsonArray)result;
        Assert.AreEqual(5, jsonArray.Elements.Count);
        Assert.IsInstanceOfType(jsonArray.Elements[0], typeof(JsonNumber));
        Assert.IsInstanceOfType(jsonArray.Elements[1], typeof(JsonString));
        Assert.IsInstanceOfType(jsonArray.Elements[2], typeof(JsonBoolean));
        Assert.IsInstanceOfType(jsonArray.Elements[3], typeof(JsonNull));
        Assert.IsInstanceOfType(jsonArray.Elements[4], typeof(JsonObject));
    }

    [TestMethod]
    public void Parse_ObjectWithEmptyArraysAndObjects_ReturnsCorrectAst()
    {
        var input = "{\"emptyArray\": [], \"emptyObject\": {}, \"nested\": {\"innerEmpty\": {}}}";
        var result = _parser.Parse(input);

        Assert.IsInstanceOfType(result, typeof(JsonObject));
        var jsonObject = (JsonObject)result;
        Assert.AreEqual(3, jsonObject.Properties.Count);

        var emptyArray = (JsonArray)jsonObject.Properties[0].Value;
        Assert.AreEqual(0, emptyArray.Elements.Count);

        var emptyObject = (JsonObject)jsonObject.Properties[1].Value;
        Assert.AreEqual(0, emptyObject.Properties.Count);
    }

    [TestInitialize]
    public void Initialize()
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
