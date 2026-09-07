using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BulkEmailSender.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSendOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SendOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalRows = table.Column<int>(type: "INTEGER", nullable: false),
                    SentRows = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedRows = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SendOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SendWorkItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowId = table.Column<long>(type: "INTEGER", nullable: false),
                    ValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SendWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SendWorkItems_SendOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "SendOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SendWorkItems_OperationId_Status",
                table: "SendWorkItems",
                columns: new[] { "OperationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SendWorkItems");

            migrationBuilder.DropTable(
                name: "SendOperations");
        }
    }
}
