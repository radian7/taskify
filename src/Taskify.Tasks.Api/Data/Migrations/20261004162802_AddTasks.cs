using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Taskify.Tasks.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "tasks",
                columns: new[] { "Id", "AssigneeUserId", "CreatedAt", "CreatedByUserId", "Description", "ProjectId", "Status", "Title", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("33333333-3333-3333-0001-000000000001"), new Guid("11111111-1111-1111-1111-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 9, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "Done", "Set up CI for iOS builds", new DateTimeOffset(new DateTime(2026, 9, 9, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000002"), new Guid("11111111-1111-1111-1111-000000000005"), new DateTimeOffset(new DateTime(2026, 9, 9, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "Done", "Design app icon", new DateTimeOffset(new DateTime(2026, 9, 9, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000003"), null, new DateTimeOffset(new DateTime(2026, 9, 9, 15, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "Done", "Create release checklist", new DateTimeOffset(new DateTime(2026, 9, 9, 15, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000004"), new Guid("11111111-1111-1111-1111-000000000003"), new DateTimeOffset(new DateTime(2026, 9, 9, 18, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "InProgress", "Implement push notifications", new DateTimeOffset(new DateTime(2026, 9, 9, 18, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000005"), new Guid("11111111-1111-1111-1111-000000000004"), new DateTimeOffset(new DateTime(2026, 9, 9, 21, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "InProgress", "Build offline mode", new DateTimeOffset(new DateTime(2026, 9, 9, 21, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000006"), new Guid("11111111-1111-1111-1111-000000000005"), new DateTimeOffset(new DateTime(2026, 9, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "InReview", "Login screen accessibility fixes", new DateTimeOffset(new DateTime(2026, 9, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000007"), new Guid("11111111-1111-1111-1111-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 10, 3, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "InReview", "Onboarding copy review", new DateTimeOffset(new DateTime(2026, 9, 10, 3, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000008"), new Guid("11111111-1111-1111-1111-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 10, 6, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "ToDo", "Write App Store description", new DateTimeOffset(new DateTime(2026, 9, 10, 6, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000009"), null, new DateTimeOffset(new DateTime(2026, 9, 10, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "ToDo", "Plan beta tester outreach", new DateTimeOffset(new DateTime(2026, 9, 10, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0001-000000000010"), new Guid("11111111-1111-1111-1111-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 10, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000001"), "ToDo", "Define crash reporting thresholds", new DateTimeOffset(new DateTime(2026, 9, 10, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000001"), new Guid("11111111-1111-1111-1111-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 16, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "Done", "Choose design system", new DateTimeOffset(new DateTime(2026, 9, 16, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000002"), new Guid("11111111-1111-1111-1111-000000000004"), new DateTimeOffset(new DateTime(2026, 9, 16, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "Done", "Set up staging site", new DateTimeOffset(new DateTime(2026, 9, 16, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000003"), new Guid("11111111-1111-1111-1111-000000000003"), new DateTimeOffset(new DateTime(2026, 9, 16, 15, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "Done", "Redirect map for old URLs", new DateTimeOffset(new DateTime(2026, 9, 16, 15, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000004"), new Guid("11111111-1111-1111-1111-000000000005"), new DateTimeOffset(new DateTime(2026, 9, 16, 18, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "InProgress", "Implement responsive navigation", new DateTimeOffset(new DateTime(2026, 9, 16, 18, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000005"), new Guid("11111111-1111-1111-1111-000000000003"), new DateTimeOffset(new DateTime(2026, 9, 16, 21, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "InProgress", "Migrate blog to new layout", new DateTimeOffset(new DateTime(2026, 9, 16, 21, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000006"), new Guid("11111111-1111-1111-1111-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "InReview", "Homepage hero section", new DateTimeOffset(new DateTime(2026, 9, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000007"), null, new DateTimeOffset(new DateTime(2026, 9, 17, 3, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "InReview", "Cookie banner wording", new DateTimeOffset(new DateTime(2026, 9, 17, 3, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000008"), new Guid("11111111-1111-1111-1111-000000000004"), new DateTimeOffset(new DateTime(2026, 9, 17, 6, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "ToDo", "Audit current page speed", new DateTimeOffset(new DateTime(2026, 9, 17, 6, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000009"), null, new DateTimeOffset(new DateTime(2026, 9, 17, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "ToDo", "Collect customer testimonials", new DateTimeOffset(new DateTime(2026, 9, 17, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0002-000000000010"), new Guid("11111111-1111-1111-1111-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 17, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000002"), "ToDo", "Draft new pricing page", new DateTimeOffset(new DateTime(2026, 9, 17, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000001"), new Guid("11111111-1111-1111-1111-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 23, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "Done", "Set up team chat channels", new DateTimeOffset(new DateTime(2026, 9, 23, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000002"), new Guid("11111111-1111-1111-1111-000000000005"), new DateTimeOffset(new DateTime(2026, 9, 23, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "Done", "Inventory of software licenses", new DateTimeOffset(new DateTime(2026, 9, 23, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000003"), null, new DateTimeOffset(new DateTime(2026, 9, 23, 15, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "Done", "Create IT help page", new DateTimeOffset(new DateTime(2026, 9, 23, 15, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000004"), new Guid("11111111-1111-1111-1111-000000000004"), new DateTimeOffset(new DateTime(2026, 9, 23, 18, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "InProgress", "Build team calendar integration", new DateTimeOffset(new DateTime(2026, 9, 23, 18, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000005"), new Guid("11111111-1111-1111-1111-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 23, 21, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "InProgress", "Clean up shared drive", new DateTimeOffset(new DateTime(2026, 9, 23, 21, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000006"), new Guid("11111111-1111-1111-1111-000000000003"), new DateTimeOffset(new DateTime(2026, 9, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "InReview", "Expense report template", new DateTimeOffset(new DateTime(2026, 9, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000007"), null, new DateTimeOffset(new DateTime(2026, 9, 24, 3, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "InReview", "Access request process", new DateTimeOffset(new DateTime(2026, 9, 24, 3, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000008"), null, new DateTimeOffset(new DateTime(2026, 9, 24, 6, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "ToDo", "Evaluate time-tracking tools", new DateTimeOffset(new DateTime(2026, 9, 24, 6, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000009"), new Guid("11111111-1111-1111-1111-000000000005"), new DateTimeOffset(new DateTime(2026, 9, 24, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "ToDo", "Write onboarding guide for new hires", new DateTimeOffset(new DateTime(2026, 9, 24, 9, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("33333333-3333-3333-0003-000000000010"), new Guid("11111111-1111-1111-1111-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 24, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("11111111-1111-1111-1111-000000000001"), null, new Guid("22222222-2222-2222-2222-000000000003"), "ToDo", "Automate weekly status report", new DateTimeOffset(new DateTime(2026, 9, 24, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_ProjectId_Status_CreatedAt",
                table: "tasks",
                columns: new[] { "ProjectId", "Status", "CreatedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
