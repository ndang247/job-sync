using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobApplicationsListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_CreatedAt_Id",
                table: "JobApplications",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0],
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobApplications_CreatedAt_Id",
                table: "JobApplications");
        }
    }
}
