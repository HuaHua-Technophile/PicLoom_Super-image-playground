using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicLoom.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Persists how many reference images a task claimed at submission. The worker uses it to refuse
    /// to silently downgrade an image-to-image task to text-to-image when the bound rows are missing.
    /// Additive only: existing rows default to zero, which is exactly "no reference images".
    /// </summary>
    [DbContext(typeof(PicLoomDbContext))]
    [Migration("20261028130000_AddExpectedReferenceCount")]
    public partial class AddExpectedReferenceCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpectedReferenceCount",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedReferenceCount",
                table: "Tasks");
        }
    }
}
