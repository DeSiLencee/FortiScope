using FortiScope.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FortiScope.Data;

public sealed class FortiScopeDbContext(DbContextOptions<FortiScopeDbContext> options) : DbContext(options)
{
    public DbSet<DeviceMetricSample> DeviceMetricSamples => Set<DeviceMetricSample>();
    public DbSet<InterfaceMetricSample> InterfaceMetricSamples => Set<InterfaceMetricSample>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<AlertSettings> AlertSettings => Set<AlertSettings>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<AlertState> AlertStates => Set<AlertState>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
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
modelBuilder.Entity<Device>(entity =>
{
    entity.Property(item => item.Name)
        .HasMaxLength(256)
        .IsRequired();

    entity.Property(item => item.IpAddress)
        .HasMaxLength(64)
        .IsRequired();

    entity.Property(item => item.SnmpVersion)
        .HasMaxLength(16);

    entity.Property(item => item.SnmpUsername)
        .HasMaxLength(128);

    entity.Property(item => item.AuthProtocol)
        .HasMaxLength(32);

    entity.Property(item => item.PrivacyProtocol)
        .HasMaxLength(32);

    entity.HasIndex(item => item.IpAddress)
        .IsUnique();
});
        modelBuilder.Entity<AlertSettings>(entity =>
        {
            entity.ToTable("AlertSettings");
        });
        modelBuilder.Entity<EmailSettings>(entity =>
        {
            entity.ToTable("EmailSettings");
            entity.Property(item => item.SmtpHost).HasMaxLength(256);
            entity.Property(item => item.Username).HasMaxLength(256);
            entity.Property(item => item.PasswordEncrypted).HasMaxLength(2048);
            entity.Property(item => item.FromAddress).HasMaxLength(320);
            entity.Property(item => item.ToAddress).HasMaxLength(1000);
        });
        modelBuilder.Entity<AlertState>(entity =>
        {
            entity.Property(item => item.StateKey).HasMaxLength(32);
            entity.Property(item => item.AlertType).HasMaxLength(64);
            entity.Property(item => item.Severity).HasMaxLength(16);
            entity.Property(item => item.InterfaceName).HasMaxLength(256);
            entity.HasIndex(item => new { item.DeviceId, item.StateKey }).IsUnique();
        });
        modelBuilder.Entity<AlertEvent>(entity =>
        {
            entity.Property(item => item.DeviceName).HasMaxLength(256);
            entity.Property(item => item.DeviceIp).HasMaxLength(64);
            entity.Property(item => item.AlertType).HasMaxLength(64);
            entity.Property(item => item.Severity).HasMaxLength(16);
            entity.Property(item => item.EventType).HasMaxLength(16);
            entity.Property(item => item.Message).HasMaxLength(1000);
            entity.Property(item => item.InterfaceName).HasMaxLength(256);
            entity.HasIndex(item => new { item.DeviceId, item.OccurredAtUtc });
            entity.HasIndex(item => item.OccurredAtUtc);
            entity.HasIndex(item => item.AlertType);
            entity.HasIndex(item => item.Severity);
        });
    }
}
