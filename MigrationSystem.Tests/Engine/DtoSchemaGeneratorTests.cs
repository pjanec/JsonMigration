using MigrationSystem.Core.Validation;
using MigrationSystem.Engine.Internal;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class DtoSchemaGeneratorTests
{
    private readonly DtoSchemaGenerator _generator = new();

    [SchemaDescription("Test DTO for validation")]
    public class TestDto
    {
        [SchemaDescription("A test string property")]
        [StringPattern(@"^\d{3}-\d{3}-\d{4}$")]
        public string PhoneNumber { get; set; } = "";

        [SchemaDescription("A test number property")]
        [NumberRange(0, 100)]
        public int Score { get; set; }

        [SchemaDescription("A test array property")]
        [ArrayCount(1, 5)]
        public string[] Tags { get; set; } = [];
    }

    [Fact]
    public void GenerateSchemaForType_AppliesCustomAttributes()
    {
        // Act
        var schema = _generator.GenerateSchemaForType(typeof(TestDto));

        // Assert
        Assert.NotNull(schema);
        Assert.Equal("Test DTO for validation", schema.Description);
        
        // Verify properties exist
        Assert.True(schema.Properties.ContainsKey("PhoneNumber") || schema.Properties.ContainsKey("phoneNumber"));
        Assert.True(schema.Properties.ContainsKey("Score") || schema.Properties.ContainsKey("score"));
        Assert.True(schema.Properties.ContainsKey("Tags") || schema.Properties.ContainsKey("tags"));
    }

    [Fact]
    public void GenerateSchemaForType_WorksWithSimpleTypes()
    {
        // Act
        var schema = _generator.GenerateSchemaForType(typeof(string));

        // Assert
        Assert.NotNull(schema);
    }
}