using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BulkEmailSender.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailDefinitionToSendOperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "SendOperations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "SendOperations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "SendOperations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentsJson",
                table: "SendOperations");

            migrationBuilder.DropColumn(
                name: "Body",
                table: "SendOperations");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "SendOperations");
        }
    }
}
