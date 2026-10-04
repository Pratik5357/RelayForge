using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelayForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "JobTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailUntilAttempt",
                table: "JobTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdempotencyKey",
                table: "JobTasks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "JobTasks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseOwnerId",
                table: "JobTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "JobTasks",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "JobTasks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_State_LeaseExpiresAt",
                table: "JobTasks",
                columns: new[] { "State", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_State_NextAttemptAt",
                table: "JobTasks",
                columns: new[] { "State", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobTasks_State_LeaseExpiresAt",
                table: "JobTasks");

            migrationBuilder.DropIndex(
                name: "IX_JobTasks_State_NextAttemptAt",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "FailUntilAttempt",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "LeaseOwnerId",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "JobTasks");
        }
    }
}
