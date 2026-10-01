using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BulkEmailSender.Api.Migrations;

[DbContext(typeof(BulkEmailSender.Api.Data.ApplicationDbContext))]
[Migration("20261001000000_AddGoogleGmailOAuth")]
public partial class AddGoogleGmailOAuth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "UserId", table: "SendOperations", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.CreateTable(
            name: "GoogleOAuthConnections",
            columns: table => new
            {
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                GoogleSubjectId = table.Column<string>(type: "TEXT", nullable: false),
                EmailAddress = table.Column<string>(type: "TEXT", nullable: false),
                AccessTokenProtected = table.Column<string>(type: "TEXT", nullable: false),
                RefreshTokenProtected = table.Column<string>(type: "TEXT", nullable: true),
                AccessTokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Scope = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            }, constraints: table => table.PrimaryKey("PK_GoogleOAuthConnections", x => x.UserId));
        migrationBuilder.CreateTable(
            name: "GoogleOAuthStates",
            columns: table => new
            {
                StateHash = table.Column<string>(type: "TEXT", nullable: false),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            }, constraints: table => table.PrimaryKey("PK_GoogleOAuthStates", x => x.StateHash));
        migrationBuilder.CreateIndex(name: "IX_GoogleOAuthConnections_GoogleSubjectId", table: "GoogleOAuthConnections", column: "GoogleSubjectId");
        migrationBuilder.CreateIndex(name: "IX_GoogleOAuthStates_ExpiresAtUtc", table: "GoogleOAuthStates", column: "ExpiresAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "GoogleOAuthConnections");
        migrationBuilder.DropTable(name: "GoogleOAuthStates");
        migrationBuilder.DropColumn(name: "UserId", table: "SendOperations");
    }
}
