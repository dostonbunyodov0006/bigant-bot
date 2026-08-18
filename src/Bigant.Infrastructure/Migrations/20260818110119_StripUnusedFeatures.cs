using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bigant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StripUnusedFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_deal_items_counterparties_source_supplier_id",
                table: "deal_items");

            migrationBuilder.DropTable(
                name: "supplier_products");

            migrationBuilder.DropIndex(
                name: "i_x_deal_items_source_supplier_id",
                table: "deal_items");

            migrationBuilder.DropColumn(
                name: "max_discount_percent",
                table: "users");

            migrationBuilder.DropColumn(
                name: "telegram_bind_code",
                table: "users");

            migrationBuilder.DropColumn(
                name: "telegram_bind_code_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "min_margin_percent",
                table: "products");

            migrationBuilder.DropColumn(
                name: "cost_total",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "cost_price",
                table: "deal_items");

            migrationBuilder.DropColumn(
                name: "source_supplier_id",
                table: "deal_items");

            migrationBuilder.DropColumn(
                name: "credit_limit",
                table: "counterparties");

            migrationBuilder.DropColumn(
                name: "price_category",
                table: "counterparties");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "max_discount_percent",
                table: "users",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "telegram_bind_code",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "telegram_bind_code_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_margin_percent",
                table: "products",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_total",
                table: "deals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_price",
                table: "deal_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_supplier_id",
                table: "deal_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credit_limit",
                table: "counterparties",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "price_category",
                table: "counterparties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    lead_days = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_supplier_products", x => x.id);
                    table.ForeignKey(
                        name: "f_k_supplier_products_counterparties_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "counterparties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_supplier_products_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_deal_items_source_supplier_id",
                table: "deal_items",
                column: "source_supplier_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_products_product_id",
                table: "supplier_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_products_supplier_id_product_id",
                table: "supplier_products",
                columns: new[] { "supplier_id", "product_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_deal_items_counterparties_source_supplier_id",
                table: "deal_items",
                column: "source_supplier_id",
                principalTable: "counterparties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
