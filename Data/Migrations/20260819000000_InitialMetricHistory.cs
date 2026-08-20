using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FortiScope.Data.Migrations;

[DbContext(typeof(FortiScopeDbContext))]
[Migration("20260819000000_InitialMetricHistory")]
public partial class InitialMetricHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeviceMetricSamples",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                DeviceIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Connected = table.Column<bool>(type: "INTEGER", nullable: false),
                CpuUsage = table.Column<int>(type: "INTEGER", nullable: true),
                MemoryUsage = table.Column<int>(type: "INTEGER", nullable: true),
                SessionCount = table.Column<long>(type: "INTEGER", nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
            }, constraints: table => table.PrimaryKey("PK_DeviceMetricSamples", item => item.Id));

        migrationBuilder.CreateTable(
            name: "InterfaceMetricSamples",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                DeviceIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                InterfaceIndex = table.Column<int>(type: "INTEGER", nullable: false),
                InterfaceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                AdminStatus = table.Column<int>(type: "INTEGER", nullable: true),
                OperStatus = table.Column<int>(type: "INTEGER", nullable: true),
                IncomingMbps = table.Column<double>(type: "REAL", nullable: false),
                OutgoingMbps = table.Column<double>(type: "REAL", nullable: false),
                TotalMbps = table.Column<double>(type: "REAL", nullable: false),
                UtilizationPercent = table.Column<double>(type: "REAL", nullable: true)
            }, constraints: table => table.PrimaryKey("PK_InterfaceMetricSamples", item => item.Id));

        migrationBuilder.CreateIndex("IX_DeviceMetricSamples_DeviceIp_TimestampUtc", "DeviceMetricSamples",
            new[] { "DeviceIp", "TimestampUtc" }, unique: true);
        migrationBuilder.CreateIndex("IX_InterfaceMetricSamples_DeviceIp_InterfaceIndex_TimestampUtc", "InterfaceMetricSamples",
            new[] { "DeviceIp", "InterfaceIndex", "TimestampUtc" }, unique: true);
        migrationBuilder.CreateIndex("IX_InterfaceMetricSamples_InterfaceIndex_TimestampUtc", "InterfaceMetricSamples",
            new[] { "InterfaceIndex", "TimestampUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("DeviceMetricSamples");
        migrationBuilder.DropTable("InterfaceMetricSamples");
    }
}
