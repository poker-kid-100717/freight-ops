using Microsoft.Extensions.DependencyInjection;
using Portfolio.Freight.Api.Data;

namespace Portfolio.Freight.Api.Tests;

public sealed class DatabaseTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task MigrateCommandRunsOnlyAgainstPostgreSqlAndIsIdempotent()
    {
        var status = Factory.Services.GetRequiredService<DatabaseStatus>();

        // `dotnet Portfolio.Freight.Api.dll migrate` (the deploy pipeline's step). On PostgreSQL the schema is already
        // current, so a second run succeeds as a no-op; the SQLite demo store has no migrations and refuses.
        Assert.Equal(status.Persistent, await Database.MigrateAsync(Factory.Services));
        Assert.Equal(status.Persistent, await Database.MigrateAsync(Factory.Services));
    }
}
