using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicLoom.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Additive migration for image-to-image inputs and the five generation parameters. Existing
    /// rows keep their data: the new task columns are nullable and <c>Assets.Ordinal</c> defaults to
    /// zero, so every historical asset remains readable.
    /// </summary>
    [DbContext(typeof(PicLoomDbContext))]
    [Migration("20261028120000_AddImageInputsAndOptions")]
    public partial class AddImageInputsAndOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Options",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActualCount",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropIndex(
                name: "IX_Assets_AttemptId_Hash",
                table: "Assets");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_AttemptId_Ordinal",
                table: "Assets",
                columns: new[] { "AttemptId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "ReferenceImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferenceImages_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceImages_TaskId_Ordinal",
                table: "ReferenceImages",
                columns: new[] { "TaskId", "Ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferenceImages");

            migrationBuilder.DropIndex(
                name: "IX_Assets_AttemptId_Ordinal",
                table: "Assets");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_AttemptId_Hash",
                table: "Assets",
                columns: new[] { "AttemptId", "Hash" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "Ordinal",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ActualCount",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Options",
                table: "Tasks");
        }
    }
}
