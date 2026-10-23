using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RelayForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2Platform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimeoutMs",
                table: "JobTasks",
                type: "integer",
                nullable: false,
                defaultValue: 300000);

            migrationBuilder.AddColumn<string>(
                name: "Isolation",
                table: "JobTasks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "JobTasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Output",
                table: "JobTasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "JobTasks",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "JobTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "JobTasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "DefinitionId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventSequence",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledFor",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "Jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // v1 tasks were all simulated, with their behavior in two columns. Move it into the
            // payload the simulated handler reads, and give every task a key (unique per job;
            // the name when that is unique, else the name plus the task id).
            migrationBuilder.Sql("""
                UPDATE "JobTasks" t
                SET    "Type"      = 'simulated',
                       "Isolation" = 'InProcess',
                       "Payload"   = jsonb_strip_nulls(jsonb_build_object(
                                         'durationMs', t."SimulatedDurationMs",
                                         'failUntilAttempt', t."FailUntilAttempt")),
                       "Key"       = CASE
                                         WHEN (SELECT count(*) FROM "JobTasks" o WHERE o."JobId" = t."JobId" AND o."Name" = t."Name") = 1
                                         THEN left(t."Name", 100)
                                         ELSE left(t."Name", 63) || '-' || t."Id"::text
                                     END;
                """);

            migrationBuilder.DropColumn(
                name: "FailUntilAttempt",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "SimulatedDurationMs",
                table: "JobTasks");

            migrationBuilder.CreateTable(
                name: "JobDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Template = table.Column<string>(type: "jsonb", nullable: false),
                    MisfirePolicy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConcurrencyPolicy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextFireAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastFiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboxEvents_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubmitKeys",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmitKeys", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SubmitKeys_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RejectedLateResult = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAttempts_JobTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "JobTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Stream = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Line = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskLogs_JobTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "JobTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_JobId_Key",
                table: "JobTasks",
                columns: new[] { "JobId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CompletedAt",
                table: "Jobs",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_DefinitionId_ScheduledFor",
                table: "Jobs",
                columns: new[] { "DefinitionId", "ScheduledFor" },
                unique: true,
                filter: "\"DefinitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobDefinitions_Name",
                table: "JobDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobDefinitions_NextFireAt",
                table: "JobDefinitions",
                column: "NextFireAt",
                filter: "\"Enabled\"");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_JobId_Sequence",
                table: "OutboxEvents",
                columns: new[] { "JobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_Undispatched",
                table: "OutboxEvents",
                column: "Id",
                filter: "\"DispatchedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubmitKeys_JobId",
                table: "SubmitKeys",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttempts_JobId",
                table: "TaskAttempts",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttempts_TaskId_Attempt",
                table: "TaskAttempts",
                columns: new[] { "TaskId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskLogs_TaskId_Attempt_Sequence",
                table: "TaskLogs",
                columns: new[] { "TaskId", "Attempt", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_JobDefinitions_DefinitionId",
                table: "Jobs",
                column: "DefinitionId",
                principalTable: "JobDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SimulatedDurationMs",
                table: "JobTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailUntilAttempt",
                table: "JobTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "JobTasks"
                SET    "SimulatedDurationMs" = COALESCE(("Payload"->>'durationMs')::int, 0),
                       "FailUntilAttempt"    = ("Payload"->>'failUntilAttempt')::int
                WHERE  "Type" = 'simulated';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_JobDefinitions_DefinitionId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "JobDefinitions");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "SubmitKeys");

            migrationBuilder.DropTable(
                name: "TaskAttempts");

            migrationBuilder.DropTable(
                name: "TaskLogs");

            migrationBuilder.DropIndex(
                name: "IX_JobTasks_JobId_Key",
                table: "JobTasks");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_CompletedAt",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_DefinitionId_ScheduledFor",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Isolation",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "Output",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "JobTasks");

            migrationBuilder.DropColumn(
                name: "DefinitionId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EventSequence",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ScheduledFor",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "TimeoutMs",
                table: "JobTasks");
        }
    }
}
