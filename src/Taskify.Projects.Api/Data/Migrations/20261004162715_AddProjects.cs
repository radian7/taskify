using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Taskify.Projects.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "projects",
                columns: new[] { "Id", "CreatedAt", "CreatedByUserId", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("22222222-2222-2222-2222-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 1, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), "Ship the first version of the Taskify mobile app.", "Mobile App Launch" },
                    { new Guid("22222222-2222-2222-2222-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 8, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), "Refresh the public website: faster, clearer and responsive.", "Website Redesign" },
                    { new Guid("22222222-2222-2222-2222-000000000003"), new DateTimeOffset(new DateTime(2026, 9, 15, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), "Small improvements that save the team time every week.", "Internal Tools" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_projects_CreatedAt",
                table: "projects",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
