using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TtnByUs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ttn_by_us",
                table: "deals",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ttn_by_us",
                table: "deals");
        }
    }
}
