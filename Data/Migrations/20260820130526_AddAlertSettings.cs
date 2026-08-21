using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortiScope.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CpuWarningPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    CpuCriticalPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    MemoryWarningPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    MemoryCriticalPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    OfflineTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertSettings");
        }
    }
}
