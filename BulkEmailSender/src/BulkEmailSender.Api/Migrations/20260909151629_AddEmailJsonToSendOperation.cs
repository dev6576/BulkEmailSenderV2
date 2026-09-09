using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BulkEmailSender.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailJsonToSendOperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailJson",
                table: "SendOperations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailJson",
                table: "SendOperations");
        }
    }
}
