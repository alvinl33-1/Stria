using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stria.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChaosExperiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetService = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FaultType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    Percent = table.Column<int>(type: "integer", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TriggeredBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClearedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChaosExperiments", x => x.Id);
                    table.CheckConstraint("ck_chaos_experiments_cleared", "(\"Status\" = 'Active' AND \"ClearedAt\" IS NULL AND \"ClearedBy\" IS NULL) OR (\"Status\" = 'Cleared' AND \"ClearedAt\" IS NOT NULL AND \"ClearedBy\" IS NOT NULL)");
                    table.CheckConstraint("ck_chaos_experiments_latency", "(\"FaultType\" = 'Latency' AND \"LatencyMs\" IS NOT NULL AND \"LatencyMs\" BETWEEN 1 AND 60000) OR (\"FaultType\" <> 'Latency' AND \"LatencyMs\" IS NULL)");
                    table.CheckConstraint("ck_chaos_experiments_lease", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("ck_chaos_experiments_percent", "\"Percent\" BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_chaos_experiments_status_code", "(\"FaultType\" = 'HttpFailure' AND \"StatusCode\" IS NOT NULL AND \"StatusCode\" BETWEEN 500 AND 599) OR (\"FaultType\" <> 'HttpFailure' AND \"StatusCode\" IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChaosExperiments_ClearedAt",
                table: "ChaosExperiments",
                column: "ClearedAt");

            migrationBuilder.CreateIndex(
                name: "ux_chaos_experiments_active_target",
                table: "ChaosExperiments",
                column: "TargetService",
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChaosExperiments");
        }
    }
}
