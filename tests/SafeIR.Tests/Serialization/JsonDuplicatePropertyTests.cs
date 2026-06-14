namespace SafeIR.Tests;

public sealed class JsonDuplicatePropertyTests
{
    [Fact]
    public void Module_rejects_duplicate_properties()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "first",
          "id": "second",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Fact]
    public void Metadata_rejects_duplicate_properties()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "metadata-dupe",
          "version": "1.0.0",
          "metadata": {
            "tag": "a",
            "tag": "b"
          },
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Theory]
    [InlineData("""
        {
          "id": "dupe-function",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "id": "other",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "id": "dupe-param",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "name": "other", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "id": "dupe-statement",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "op": "expr", "value": { "i32": 1 } }]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "id": "dupe-call",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "math.abs", "call": "math.max", "args": [{ "i32": 1 }] } }]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "id": "dupe-type",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "name": "Map", "arguments": ["I32"] },
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """)]
    public void Nested_shapes_reject_duplicate_properties(string json)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }
}
