using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelayForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClaimOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobTasks_Ready",
                table: "JobTasks");

            migrationBuilder.AddColumn<double>(
                name: "ClaimOrder",
                table: "JobTasks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // Waiting work keeps its place: the same order the old "priority + age / 60 s" sort
            // gave it (Fairness:AgingSeconds defaults to 60).
            migrationBuilder.Sql("""
                UPDATE "JobTasks"
                SET    "ClaimOrder" = EXTRACT(EPOCH FROM COALESCE("ReadyAt", "CreatedAt")) - "Priority" * 60
                WHERE  "State" = 'Ready';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_Ready",
                table: "JobTasks",
                column: "ClaimOrder",
                filter: "\"State\" = 'Ready'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobTasks_Ready",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "ClaimOrder",
                table: "JobTasks");

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_Ready",
                table: "JobTasks",
                column: "ReadyAt",
                filter: "\"State\" = 'Ready'");
        }
    }
}
