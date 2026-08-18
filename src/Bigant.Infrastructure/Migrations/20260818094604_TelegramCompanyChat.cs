using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramCompanyChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "telegram_chat_id",
                table: "company_profile",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "telegram_chat_id",
                table: "company_profile");
        }
    }
}
