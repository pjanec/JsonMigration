using MigrationSystem.Core.Contracts;
using MigrationSystem.Engine.Internal;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class MigrationRegistryTests
{
    // Test DTOs
    public class UserV1_0
    {
        public string Name { get; set; } = "";
    }

    public class UserV1_1 
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class UserV2_0
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    // Test migrations
    public class UserV1_0ToV1_1Migration : IJsonMigration<UserV1_0, UserV1_1>
    {
        public Task<UserV1_1> ApplyAsync(UserV1_0 fromDto)
        {
            return Task.FromResult(new UserV1_1
            {
                Name = fromDto.Name,
                Email = "unknown@example.com"
            });
        }

        public Task<UserV1_0> ReverseAsync(UserV1_1 toDto)
        {
            return Task.FromResult(new UserV1_0 { Name = toDto.Name });
        }
    }

    public class UserV1_1ToV2_0Migration : IJsonMigration<UserV1_1, UserV2_0>
    {
        public Task<UserV2_0> ApplyAsync(UserV1_1 fromDto)
        {
            return Task.FromResult(new UserV2_0
            {
                FullName = fromDto.Name,
                Email = fromDto.Email,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<UserV1_1> ReverseAsync(UserV2_0 toDto)
        {
            return Task.FromResult(new UserV1_1 
            { 
                Name = toDto.FullName, 
                Email = toDto.Email 
            });
        }
    }

    [Fact]
    public void GetTypeForVersion_ReturnsCorrectType()
    {
        // Arrange
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());

        // Act
        var type = registry.GetTypeForVersion("User", "1.0");

        // Assert
        Assert.Equal(typeof(UserV1_0), type);
    }

    [Fact]
    public void FindPath_SingleStep_ReturnsCorrectMigration()
    {
        // Arrange
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());

        // Act
        var path = registry.FindPath(typeof(UserV1_0), typeof(UserV1_1));

        // Assert
        Assert.Single(path);
        Assert.IsType<UserV1_0ToV1_1Migration>(path[0]);
    }

    [Fact]
    public void FindPath_MultiStep_ReturnsCorrectMigrationChain()
    {
        // Arrange
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());

        // Act
        var path = registry.FindPath(typeof(UserV1_0), typeof(UserV2_0));

        // Assert
        Assert.Equal(2, path.Count);
        Assert.IsType<UserV1_0ToV1_1Migration>(path[0]);
        Assert.IsType<UserV1_1ToV2_0Migration>(path[1]);
    }

    [Fact]
    public void FindPath_SameType_ReturnsEmptyPath()
    {
        // Arrange
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());

        // Act
        var path = registry.FindPath(typeof(UserV1_0), typeof(UserV1_0));

        // Assert
        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_NoPathExists_ThrowsException()
    {
        // Arrange
        var registry = new MigrationRegistry();
        // Note: Not registering any migrations

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.FindPath(typeof(UserV1_0), typeof(UserV2_0)));
        
        Assert.Contains("No migration path could be found", exception.Message);
    }
}