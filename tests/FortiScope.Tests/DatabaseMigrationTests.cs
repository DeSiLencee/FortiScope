using FortiScope.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FortiScope.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task InitialMetricHistoryMigration_CreatesBothTables()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"fortiscope-test-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<FortiScopeDbContext>()
                .UseSqlite($"Data Source={databasePath}").Options;
            await using var dbContext = new FortiScopeDbContext(options);

            await dbContext.Database.MigrateAsync();

            Assert.Empty(await dbContext.DeviceMetricSamples.AsNoTracking().ToListAsync());
            Assert.Empty(await dbContext.InterfaceMetricSamples.AsNoTracking().ToListAsync());
            Assert.Contains("20260819000000_InitialMetricHistory",
                await dbContext.Database.GetAppliedMigrationsAsync());
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}
