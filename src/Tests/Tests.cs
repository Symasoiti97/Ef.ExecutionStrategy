using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;
using Tests.Database;

namespace Tests;

public class Tests
{
    private IServiceProvider _serviceProvider = null!;
    private readonly Faker _faker = new();

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql("Server=localhost;Port=15434;User Id=pgsync;Password=PLEASE_CHANGE_ME;Database=StrategyTest;",
                o => o.EnableRetryOnFailure(3, TimeSpan.FromMinutes(1), new List<string>
                {
                    /*"57P01"*/
                }));
        });
        services.AddLogging(x => x.AddConsole());

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCaseSource(typeof(TestsDataSource), nameof(TestsDataSource.ExecutionStrategyInTransactionTestCaseSource))]
    public async Task ExecutionStrategyTest(Entity entityUpdate, Entity entityRemove, Entity entityAdd)
    {
        var appDbContext = _serviceProvider.GetRequiredService<AppDbContext>();

        #region Init data

        appDbContext.Add(entityUpdate);
        appDbContext.Add(entityRemove);
        await appDbContext.SaveChangesAsync();
        appDbContext.ChangeTracker.Clear();

        #endregion

        var isError = true;
        var executionStrategy = appDbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await appDbContext.Database.BeginTransactionAsync();

            var entity = new Entity {Id = entityRemove.Id, Number = new Random().Next(1, 5)};
            appDbContext.Remove(entity);
            entityUpdate.Name = _faker.Random.Word();
            appDbContext.Update(entityUpdate);

            if (isError)
            {
                isError = false;
                throw new NpgsqlException(nameof(NpgsqlException), new IOException(nameof(IOException)));
            }

            await appDbContext.SaveChangesAsync(acceptAllChangesOnSuccess: true);

            await transaction.CommitAsync();
        });
    }

    [TestCaseSource(typeof(TestsDataSource), nameof(TestsDataSource.ExecutionStrategyInTransactionTestCaseSource))]
    public async Task ExecutionStrategyInTransactionTest(Entity entityUpdate, Entity entityRemove, Entity entityAdd)
    {
        var appDbContext = _serviceProvider.GetRequiredService<AppDbContext>();

        //Init data
        appDbContext.Add(entityUpdate);
        appDbContext.Add(entityRemove);
        await appDbContext.SaveChangesAsync();
        appDbContext.ChangeTracker.Clear();

        //Start
        appDbContext.Add(entityAdd);
        appDbContext.Remove(entityRemove);
        var entityUpdateFromDb = await appDbContext.Set<Entity>().SingleAsync(x => x.Id == entityUpdate.Id);
        entityUpdateFromDb.Name = _faker.Random.Word();

        var isError = false;
        var executionStrategy = appDbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteInTransactionAsync(appDbContext, async (context, token) =>
        {
            await context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: token);

            if (isError)
            {
                isError = false;
                throw new NpgsqlException(nameof(NpgsqlException), new IOException(nameof(IOException)));
            }
        }, verifySucceeded: async (context, token) => await context.Set<Entity>().AnyAsync(x => x.Id == entityAdd.Id, cancellationToken: token));

        appDbContext.ChangeTracker.AcceptAllChanges();
    }
}