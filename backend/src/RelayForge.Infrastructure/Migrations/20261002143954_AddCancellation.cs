using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelayForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancellationRequestedAt",
                table: "Jobs",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationRequestedAt",
                table: "Jobs");
        }
    }
}
