namespace Tnzi.Localization.Tests.Json;

/// <summary>
/// Tests for JsonStringLocalizer.FlattenJsonElement - nested JSON flattening with dot notation
/// </summary>
public class JsonStringLocalizerFlattenTests
{
    [Fact]
    public void FlattenJsonElement_FlatJson_ReturnsSameKeys()
    {
        // Arrange
        var json = """{"Hello": "World", "Goodbye": "Farewell"}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("World", result["Hello"]);
        Assert.Equal("Farewell", result["Goodbye"]);
    }

    [Fact]
    public void FlattenJsonElement_NestedOneLevel_UsesDotNotation()
    {
        // Arrange
        var json = """{"Auth": {"Login": "Sign In", "Logout": "Sign Out"}}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Sign In", result["Auth.Login"]);
        Assert.Equal("Sign Out", result["Auth.Logout"]);
    }

    [Fact]
    public void FlattenJsonElement_DeeplyNested_FlattensCorrectly()
    {
        // Arrange
        var json = """{"App": {"Settings": {"Theme": {"Dark": "Dark Mode", "Light": "Light Mode"}}}}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Dark Mode", result["App.Settings.Theme.Dark"]);
        Assert.Equal("Light Mode", result["App.Settings.Theme.Light"]);
    }

    [Fact]
    public void FlattenJsonElement_MixedFlatAndNested_HandlesAll()
    {
        // Arrange
        var json = """{"Title": "My App", "Auth": {"Login": "Sign In"}, "Footer": "Copyright 2026"}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("My App", result["Title"]);
        Assert.Equal("Sign In", result["Auth.Login"]);
        Assert.Equal("Copyright 2026", result["Footer"]);
    }

    [Fact]
    public void FlattenJsonElement_EmptyObject_ReturnsEmpty()
    {
        // Arrange
        var json = """{}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FlattenJsonElement_NestedEmptyObject_ReturnsEmpty()
    {
        // Arrange
        var json = """{"Auth": {}}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FlattenJsonElement_NumberValue_ConvertsToString()
    {
        // Arrange
        var json = """{"Config": {"MaxRetries": 3, "Timeout": 30.5}}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("3", result["Config.MaxRetries"]);
        Assert.Equal("30.5", result["Config.Timeout"]);
    }

    [Fact]
    public void FlattenJsonElement_BooleanValue_ConvertsToLowerCase()
    {
        // Arrange
        var json = """{"Feature": {"Enabled": true, "Debug": false}}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("true", result["Feature.Enabled"]);
        Assert.Equal("false", result["Feature.Debug"]);
    }

    [Fact]
    public void FlattenJsonElement_NullValue_IsSkipped()
    {
        // Arrange
        var json = """{"Title": "Hello", "Description": null}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Single(result);
        Assert.Equal("Hello", result["Title"]);
        Assert.False(result.ContainsKey("Description"));
    }

    [Fact]
    public void FlattenJsonElement_ArrayValue_FlattensWithNumericIndex()
    {
        // Arrange
        var json = """{"Errors": {"Required": ["Field is required", "Cannot be empty"]}}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Field is required", result["Errors.Required.0"]);
        Assert.Equal("Cannot be empty", result["Errors.Required.1"]);
    }

    [Fact]
    public void FlattenJsonElement_WithPrefix_PrependsPrefix()
    {
        // Arrange
        var json = """{"Login": "Sign In"}""";
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, "Auth", result);

        // Assert
        Assert.Single(result);
        Assert.Equal("Sign In", result["Auth.Login"]);
    }

    [Fact]
    public void FlattenJsonElement_ComplexRealWorld_HandlesCorrectly()
    {
        // Arrange: typical i18n JSON file
        var json = """
        {
            "Common": {
                "Save": "Save",
                "Cancel": "Cancel",
                "Delete": "Delete"
            },
            "Auth": {
                "Login": {
                    "Title": "Sign In",
                    "Username": "Username",
                    "Password": "Password"
                },
                "Register": {
                    "Title": "Sign Up"
                }
            },
            "AppName": "My Application"
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        // Act
        JsonStringLocalizer.FlattenJsonElement(doc.RootElement, string.Empty, result);

        // Assert: 3 (Common) + 3 (Auth.Login) + 1 (Auth.Register) + 1 (AppName) = 8
        Assert.Equal(8, result.Count);
        Assert.Equal("Save", result["Common.Save"]);
        Assert.Equal("Cancel", result["Common.Cancel"]);
        Assert.Equal("Delete", result["Common.Delete"]);
        Assert.Equal("Sign In", result["Auth.Login.Title"]);
        Assert.Equal("Username", result["Auth.Login.Username"]);
        Assert.Equal("Password", result["Auth.Login.Password"]);
        Assert.Equal("Sign Up", result["Auth.Register.Title"]);
        Assert.Equal("My Application", result["AppName"]);
    }
}
