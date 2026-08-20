using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FortiScope.Data.Migrations;

[DbContext(typeof(FortiScopeDbContext))]
public partial class FortiScopeDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.22");

        modelBuilder.Entity("FortiScope.Data.Entities.DeviceMetricSample", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            entity.Property<bool>("Connected").HasColumnType("INTEGER");
            entity.Property<int?>("CpuUsage").HasColumnType("INTEGER");
            entity.Property<string>("DeviceIp").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            entity.Property<string>("DeviceName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            entity.Property<string>("ErrorMessage").HasMaxLength(1000).HasColumnType("TEXT");
            entity.Property<int?>("MemoryUsage").HasColumnType("INTEGER");
            entity.Property<long?>("SessionCount").HasColumnType("INTEGER");
            entity.Property<DateTime>("TimestampUtc").HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("DeviceIp", "TimestampUtc").IsUnique();
            entity.ToTable("DeviceMetricSamples");
        });

        modelBuilder.Entity("FortiScope.Data.Entities.InterfaceMetricSample", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            entity.Property<int?>("AdminStatus").HasColumnType("INTEGER");
            entity.Property<string>("DeviceIp").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            entity.Property<double>("IncomingMbps").HasColumnType("REAL");
            entity.Property<int>("InterfaceIndex").HasColumnType("INTEGER");
            entity.Property<string>("InterfaceName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            entity.Property<int?>("OperStatus").HasColumnType("INTEGER");
            entity.Property<double>("OutgoingMbps").HasColumnType("REAL");
            entity.Property<DateTime>("TimestampUtc").HasColumnType("TEXT");
            entity.Property<double>("TotalMbps").HasColumnType("REAL");
            entity.Property<double?>("UtilizationPercent").HasColumnType("REAL");
            entity.HasKey("Id");
            entity.HasIndex("InterfaceIndex", "TimestampUtc");
            entity.HasIndex("DeviceIp", "InterfaceIndex", "TimestampUtc").IsUnique();
            entity.ToTable("InterfaceMetricSamples");
        });
    }
}
