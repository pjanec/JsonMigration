using MigrationSystem.Core.Contracts;
using MigrationSystem.TestHelpers.Fluent;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.TestHelpers;

public class MigrationTestBuilderTests
{
    // Test DTOs
    public class PersonV1
    {
        public string Name { get; set; } = "";
    }

    public class PersonV2 
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
    }

    // Test migration
    public class PersonV1ToV2Migration : IJsonMigration<PersonV1, PersonV2>
    {
        public Task<PersonV2> ApplyAsync(PersonV1 fromDto)
        {
            return Task.FromResult(new PersonV2
            {
                FullName = fromDto.Name,
                Email = "unknown@example.com"
            });
        }

        public Task<PersonV1> ReverseAsync(PersonV2 toDto)
        {
            return Task.FromResult(new PersonV1 { Name = toDto.FullName });
        }
    }

    [Fact]
    public async Task MigrationTestBuilder_CanTestMigrations()
    {
        // Arrange
        var migration = new PersonV1ToV2Migration();
        var input = new PersonV1 { Name = "John Doe" };
        var expectedOutput = new PersonV2 
        { 
            FullName = "John Doe", 
            Email = "unknown@example.com" 
        };

        // Act & Assert
        await MigrationTestBuilder
            .For(migration)
            .From(input)
            .Produces(expectedOutput);
    }
}