using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectJobApplicationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "JobApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "JobApplications" AS ja
                SET "UserId" = ec."UserId"
                FROM "EmailConnections" AS ec
                WHERE ja."EmailConnectionId" = ec."Id";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "JobApplications",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_UserId_CreatedAt_Id",
                table: "JobApplications",
                columns: new[] { "UserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_JobApplications_Users_UserId",
                table: "JobApplications",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobApplications_Users_UserId",
                table: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_UserId_CreatedAt_Id",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "JobApplications");
        }
    }
}
