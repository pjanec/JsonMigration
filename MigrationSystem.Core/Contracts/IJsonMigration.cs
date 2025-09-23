namespace MigrationSystem.Core.Contracts;

public interface IJsonMigration<TFrom, TTo> where TFrom : class where TTo : class
{
    Task<TTo> ApplyAsync(TFrom fromDto);
    Task<TFrom> ReverseAsync(TTo toDto);
}
