using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelayForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableReadyQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobTasks_State_NextAttemptAt",
                table: "JobTasks");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadyAt",
                table: "JobTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_Ready",
                table: "JobTasks",
                column: "ReadyAt",
                filter: "\"State\" = 'Ready'");

            // Carry in-flight work over to the durable queue. Before this migration, readiness
            // lived only in process memory, so a Pending task whose dependencies had all
            // succeeded -- including a retry waiting out its backoff -- is Ready now.
            migrationBuilder.Sql("""
                UPDATE "JobTasks" t
                SET    "State" = 'Ready', "ReadyAt" = COALESCE(t."NextAttemptAt", now())
                FROM   "Jobs" j
                WHERE  j."Id" = t."JobId"
                  AND  j."State" IN ('Pending', 'Running')
                  AND  t."State" = 'Pending'
                  AND  NOT EXISTS (
                         SELECT 1
                         FROM   "TaskDependencies" d
                         JOIN   "JobTasks" p ON p."Id" = d."DependsOnJobTaskId"
                         WHERE  d."JobTaskId" = t."Id"
                           AND  p."State" <> 'Succeeded');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "JobTasks" SET "State" = 'Pending' WHERE "State" = 'Ready';""");

            migrationBuilder.DropIndex(
                name: "IX_JobTasks_Ready",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "ReadyAt",
                table: "JobTasks");

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_State_NextAttemptAt",
                table: "JobTasks",
                columns: new[] { "State", "NextAttemptAt" });
        }
    }
}
