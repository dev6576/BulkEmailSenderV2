using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BulkEmailSender.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationTimestampsAndRetryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "SendWorkItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "SendOperations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "SendOperations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "SendWorkItems");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SendOperations");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "SendOperations");
        }
    }
}
