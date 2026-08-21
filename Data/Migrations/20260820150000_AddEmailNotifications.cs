using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortiScope.Data.Migrations;

[DbContext(typeof(FortiScopeDbContext))]
[Migration("20260820150000_AddEmailNotifications")]
public sealed class AddEmailNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AlertStates",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                StateKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                AlertType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                FirstTriggeredUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastTriggeredUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastNotificationUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                LastValue = table.Column<double>(type: "REAL", nullable: true),
                Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
            }, constraints: table => table.PrimaryKey("PK_AlertStates", item => item.Id));
        migrationBuilder.CreateTable(
            name: "EmailSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                SmtpHost = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                SmtpPort = table.Column<int>(type: "INTEGER", nullable: false),
                UseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                Username = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                PasswordEncrypted = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                FromAddress = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                ToAddress = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                SendWarningAlerts = table.Column<bool>(type: "INTEGER", nullable: false),
                SendCriticalAlerts = table.Column<bool>(type: "INTEGER", nullable: false),
                SendRecoveryNotifications = table.Column<bool>(type: "INTEGER", nullable: false),
                CooldownMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            }, constraints: table => table.PrimaryKey("PK_EmailSettings", item => item.Id));
        migrationBuilder.CreateIndex("IX_AlertStates_DeviceId_StateKey", "AlertStates",
            new[] { "DeviceId", "StateKey" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AlertStates");
        migrationBuilder.DropTable(name: "EmailSettings");
    }
}
