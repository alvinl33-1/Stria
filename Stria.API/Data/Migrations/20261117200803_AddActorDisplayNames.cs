using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stria.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActorDisplayNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClearedByName",
                table: "ChaosExperiments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggeredByName",
                table: "ChaosExperiments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClearedByName",
                table: "ChaosExperiments");

            migrationBuilder.DropColumn(
                name: "TriggeredByName",
                table: "ChaosExperiments");
        }
    }
}
