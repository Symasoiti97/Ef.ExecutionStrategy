using System.Collections.Generic;
using Bogus;
using Tests.Database;

namespace Tests;

public static class TestsDataSource
{
    private static readonly Faker _faker = new Faker();

    private static Entity CreateRandomEntity() => new Entity
    {
        Id = _faker.Random.Guid(),
        Name = _faker.Random.Word(),
        Number = _faker.Random.Number()
    };

    public static IEnumerable<object> ExecutionStrategyInTransactionTestCaseSource()
    {
        yield return new[]
        {
            CreateRandomEntity(),
            CreateRandomEntity(),
            CreateRandomEntity()
        };
    }
}