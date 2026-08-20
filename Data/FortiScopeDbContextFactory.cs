using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FortiScope.Data;

public sealed class FortiScopeDbContextFactory : IDesignTimeDbContextFactory<FortiScopeDbContext>
{
    public FortiScopeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FortiScopeDbContext>()
            .UseSqlite("Data Source=data/fortiscope.db")
            .Options;
        return new FortiScopeDbContext(options);
    }
}
