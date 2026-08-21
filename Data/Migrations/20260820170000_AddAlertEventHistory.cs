using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortiScope.Data.Migrations;

[DbContext(typeof(FortiScopeDbContext))]
[Migration("20260820170000_AddAlertEventHistory")]
public sealed class AddAlertEventHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AlertEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                DeviceIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                AlertType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                EventType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                MetricValue = table.Column<double>(type: "REAL", nullable: true),
                ThresholdValue = table.Column<double>(type: "REAL", nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            }, constraints: table => table.PrimaryKey("PK_AlertEvents", item => item.Id));
        migrationBuilder.CreateIndex("IX_AlertEvents_AlertType", "AlertEvents", "AlertType");
        migrationBuilder.CreateIndex("IX_AlertEvents_DeviceId_OccurredAtUtc", "AlertEvents",
            new[] { "DeviceId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex("IX_AlertEvents_OccurredAtUtc", "AlertEvents", "OccurredAtUtc");
        migrationBuilder.CreateIndex("IX_AlertEvents_Severity", "AlertEvents", "Severity");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("AlertEvents");
}
