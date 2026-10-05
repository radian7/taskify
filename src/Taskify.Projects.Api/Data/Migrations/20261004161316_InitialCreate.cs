using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Taskify.Projects.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "DisplayName", "Role" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-000000000001"), "Maya Chen", "ProductManager" },
                    { new Guid("11111111-1111-1111-1111-000000000002"), "Liam Novak", "Engineer" },
                    { new Guid("11111111-1111-1111-1111-000000000003"), "Priya Patel", "Engineer" },
                    { new Guid("11111111-1111-1111-1111-000000000004"), "Tomasz Wiśniewski", "Engineer" },
                    { new Guid("11111111-1111-1111-1111-000000000005"), "Jordan Lee", "Engineer" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_OccurredAt",
                table: "outbox_messages",
                column: "OccurredAt",
                filter: "\"DispatchedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_DisplayName",
                table: "users",
                column: "DisplayName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
