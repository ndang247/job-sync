using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TokenExpiresAt",
                table: "Users");

            migrationBuilder.AddColumn<Guid>(
                name: "EmailConnectionId",
                table: "SyncJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "EmailConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<string>(type: "text", nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: false),
                    GrantedScopes = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_EmailConnectionId",
                table: "SyncJobs",
                column: "EmailConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailConnections_UserId_SubjectId",
                table: "EmailConnections",
                columns: new[] { "UserId", "SubjectId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_EmailConnections_EmailConnectionId",
                table: "SyncJobs",
                column: "EmailConnectionId",
                principalTable: "EmailConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_EmailConnections_EmailConnectionId",
                table: "SyncJobs");

            migrationBuilder.DropTable(
                name: "EmailConnections");

            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_EmailConnectionId",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "EmailConnectionId",
                table: "SyncJobs");

            migrationBuilder.AddColumn<string>(
                name: "AccessToken",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "TokenExpiresAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
