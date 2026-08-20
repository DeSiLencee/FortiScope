using FortiScope.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FortiScope.Data;

public sealed class FortiScopeDbContext(DbContextOptions<FortiScopeDbContext> options) : DbContext(options)
{
    public DbSet<DeviceMetricSample> DeviceMetricSamples => Set<DeviceMetricSample>();
    public DbSet<InterfaceMetricSample> InterfaceMetricSamples => Set<InterfaceMetricSample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceMetricSample>(entity =>
        {
            entity.Property(item => item.DeviceIp).HasMaxLength(64);
            entity.Property(item => item.DeviceName).HasMaxLength(256);
            entity.Property(item => item.ErrorMessage).HasMaxLength(1000);
            entity.HasIndex(item => new { item.DeviceIp, item.TimestampUtc }).IsUnique();
        });

        modelBuilder.Entity<InterfaceMetricSample>(entity =>
        {
            entity.Property(item => item.DeviceIp).HasMaxLength(64);
            entity.Property(item => item.InterfaceName).HasMaxLength(256);
            entity.HasIndex(item => new { item.DeviceIp, item.InterfaceIndex, item.TimestampUtc }).IsUnique();
            entity.HasIndex(item => new { item.InterfaceIndex, item.TimestampUtc });
        });
    }
}
