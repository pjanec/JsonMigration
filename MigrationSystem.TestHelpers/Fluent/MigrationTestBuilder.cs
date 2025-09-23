using FluentAssertions;
using MigrationSystem.Core.Contracts;
using System.Threading.Tasks;

namespace MigrationSystem.TestHelpers.Fluent;

public static class MigrationTestBuilder
{
    public static MigrationAssertion<TFrom, TTo> For<TFrom, TTo>(IJsonMigration<TFrom, TTo> migration)
        where TFrom : class where TTo : class
    {
        return new MigrationAssertion<TFrom, TTo>(migration);
    }
}

public class MigrationAssertion<TFrom, TTo>
    where TFrom : class where TTo : class
{
    private readonly IJsonMigration<TFrom, TTo> _migration;
    private TFrom _fromDto;

    public MigrationAssertion(IJsonMigration<TFrom, TTo> migration)
    {
        _migration = migration;
    }

    public MigrationAssertion<TFrom, TTo> From(TFrom fromDto)
    {
        _fromDto = fromDto;
        return this;
    }

    public async Task Produces(TTo expectedToDto)
    {
        var result = await _migration.ApplyAsync(_fromDto);
        result.Should().BeEquivalentTo(expectedToDto);
    }
}