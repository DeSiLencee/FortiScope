using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortiScope.Data.Migrations;

[DbContext(typeof(FortiScopeDbContext))]
[Migration("20260821100000_AddInterfaceTrafficAlerts")]
public sealed class AddInterfaceTrafficAlerts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("InterfaceUtilizationWarningPercent", "AlertSettings", "INTEGER",
            nullable: false, defaultValue: 70);
        migrationBuilder.AddColumn<int>("InterfaceUtilizationCriticalPercent", "AlertSettings", "INTEGER",
            nullable: false, defaultValue: 90);
        migrationBuilder.AddColumn<int>("InterfaceIndex", "AlertEvents", "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>("InterfaceName", "AlertEvents", "TEXT", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<int>("InterfaceIndex", "AlertStates", "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>("InterfaceName", "AlertStates", "TEXT", maxLength: 256, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("InterfaceUtilizationWarningPercent", "AlertSettings");
        migrationBuilder.DropColumn("InterfaceUtilizationCriticalPercent", "AlertSettings");
        migrationBuilder.DropColumn("InterfaceIndex", "AlertEvents");
        migrationBuilder.DropColumn("InterfaceName", "AlertEvents");
        migrationBuilder.DropColumn("InterfaceIndex", "AlertStates");
        migrationBuilder.DropColumn("InterfaceName", "AlertStates");
    }
}
