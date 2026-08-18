using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bigant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    entity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    old_value = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    new_value = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company_profile",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tin = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    branch_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    branch_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    account = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    bank_id = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    region_id = table.Column<int>(type: "integer", nullable: true),
                    region_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    district_code = table.Column<int>(type: "integer", nullable: true),
                    district_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    oked = table.Column<int>(type: "integer", nullable: true),
                    director_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    director_pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    accountant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    accountant_pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    vat_reg_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vat_reg_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    taxpayer_type = table.Column<int>(type: "integer", nullable: false),
                    work_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    mobile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    warehouse_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_company_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    for_sale = table.Column<bool>(type: "boolean", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_contract_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "counterparties",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    inn = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    is_supplier = table.Column<bool>(type: "boolean", nullable: false),
                    is_client = table.Column<bool>(type: "boolean", nullable: false),
                    bank_account = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    bank_mfo = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    director_name = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    director_pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    accountant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    accountant_pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    vat_reg_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vat_reg_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    taxpayer_type = table.Column<int>(type: "integer", nullable: false),
                    is_vat_payer = table.Column<bool>(type: "boolean", nullable: false),
                    oked = table.Column<int>(type: "integer", nullable: true),
                    branch_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    branch_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    region_id = table.Column<int>(type: "integer", nullable: true),
                    region_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    district_code = table.Column<int>(type: "integer", nullable: true),
                    district_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    work_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_days = table.Column<int>(type: "integer", nullable: false),
                    price_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    block_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_counterparties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    position = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    passport_series = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    passport_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    passport_issued_by = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    passport_issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_driver = table.Column<bool>(type: "boolean", nullable: false),
                    driver_license = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    system = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_id = table.Column<long>(type: "bigint", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    request_body = table.Column<string>(type: "text", nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_integration_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_counters",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_number_counters", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "product_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    mxik = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_groups", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_groups_product_groups_parent_id",
                        column: x => x.parent_id,
                        principalTable: "product_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    max_discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    telegram_chat_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    telegram_username = table.Column<string>(type: "text", nullable: true),
                    telegram_bind_code = table.Column<string>(type: "text", nullable: true),
                    telegram_bind_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    telegram_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    plate_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    trailer_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    capacity_tons = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    carrier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    carrier_inn = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_vehicles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract_template_parts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_id = table.Column<long>(type: "bigint", nullable: false),
                    ord_no = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_contract_template_parts", x => x.id);
                    table.ForeignKey(
                        name: "f_k_contract_template_parts_contract_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "contract_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    group_id = table.Column<long>(type: "bigint", nullable: true),
                    article = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    mxik = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    mxik_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    measure_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    package_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    package_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    package_qty = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    excise_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    base_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_price_fixed = table.Column<bool>(type: "boolean", nullable: false),
                    min_margin_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_products", x => x.id);
                    table.ForeignKey(
                        name: "f_k_products_product_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "product_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "deals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    counterparty_id = table.Column<long>(type: "bigint", nullable: false),
                    manager_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    parent_deal_id = table.Column<long>(type: "bigint", nullable: true),
                    contract_by_us = table.Column<bool>(type: "boolean", nullable: true),
                    contract_date = table.Column<DateOnly>(type: "date", nullable: true),
                    contract_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    contract_place = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contract_expire_date = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    payment_days = table.Column<int>(type: "integer", nullable: false),
                    prepay_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    delivery_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivery_terms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_deals", x => x.id);
                    table.ForeignKey(
                        name: "f_k_deals_counterparties_counterparty_id",
                        column: x => x.counterparty_id,
                        principalTable: "counterparties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_deals_deals_parent_deal_id",
                        column: x => x.parent_deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_deals_users_manager_id",
                        column: x => x.manager_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    deal_id = table.Column<long>(type: "bigint", nullable: true),
                    document_id = table.Column<long>(type: "bigint", nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_pushed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_notifications", x => x.id);
                    table.ForeignKey(
                        name: "f_k_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    lead_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "contract_parts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deal_id = table.Column<long>(type: "bigint", nullable: false),
                    ord_no = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_contract_parts", x => x.id);
                    table.ForeignKey(
                        name: "f_k_contract_parts_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deal_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deal_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    qty = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    source_supplier_id = table.Column<long>(type: "bigint", nullable: true),
                    mxik_snapshot = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    unit_snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    unit_code_snapshot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    name_snapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    excise_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    excise_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    package_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    marking_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_deal_items", x => x.id);
                    table.ForeignKey(
                        name: "f_k_deal_items_counterparties_source_supplier_id",
                        column: x => x.source_supplier_id,
                        principalTable: "counterparties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_deal_items_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_deal_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deal_id = table.Column<long>(type: "bigint", nullable: true),
                    doc_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    doc_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    counterparty_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    didox_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    signed_data = table.Column<string>(type: "text", nullable: true),
                    signed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    file_path = table.Column<string>(type: "text", nullable: true),
                    parent_document_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_documents", x => x.id);
                    table.ForeignKey(
                        name: "f_k_documents_counterparties_counterparty_id",
                        column: x => x.counterparty_id,
                        principalTable: "counterparties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_documents_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_documents_documents_parent_document_id",
                        column: x => x.parent_document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    counterparty_id = table.Column<long>(type: "bigint", nullable: false),
                    deal_id = table.Column<long>(type: "bigint", nullable: true),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_doc_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    matched_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_payments_counterparties_counterparty_id",
                        column: x => x.counterparty_id,
                        principalTable: "counterparties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payments_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "deal_steps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deal_id = table.Column<long>(type: "bigint", nullable: false),
                    step = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_id = table.Column<long>(type: "bigint", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_deal_steps", x => x.id);
                    table.ForeignKey(
                        name: "f_k_deal_steps_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_deal_steps_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "didox_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_try_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_didox_outbox", x => x.id);
                    table.ForeignKey(
                        name: "f_k_didox_outbox_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    uploaded_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_document_files", x => x.id);
                    table.ForeignKey(
                        name: "f_k_document_files_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "doverennost_info",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: true),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    employee_pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    position = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    passport_series = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    passport_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    passport_issued_by = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    passport_issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    basis_document = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_doverennost_info", x => x.id);
                    table.ForeignKey(
                        name: "f_k_doverennost_info_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_doverennost_info_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ttn_info",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<long>(type: "bigint", nullable: false),
                    driver_id = table.Column<long>(type: "bigint", nullable: true),
                    vehicle_id = table.Column<long>(type: "bigint", nullable: true),
                    driver_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    driver_pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    driver_license = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vehicle_model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    vehicle_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    trailer_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    trailer_model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    load_address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    load_region_id = table.Column<int>(type: "integer", nullable: true),
                    load_region_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    load_district_code = table.Column<int>(type: "integer", nullable: true),
                    load_district_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    unload_address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    unload_region_id = table.Column<int>(type: "integer", nullable: true),
                    unload_region_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    unload_district_code = table.Column<int>(type: "integer", nullable: true),
                    unload_district_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ship_date = table.Column<DateOnly>(type: "date", nullable: true),
                    distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    weight_brutto = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    weight_netto = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    transport_type = table.Column<int>(type: "integer", nullable: false),
                    carrier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    carrier_inn = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    freight_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    responsible_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    responsible_pinfl = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ttn_info", x => x.id);
                    table.ForeignKey(
                        name: "f_k_ttn_info_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_ttn_info_employees_driver_id",
                        column: x => x.driver_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_ttn_info_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_audit_log_at",
                table: "audit_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "i_x_audit_log_entity_entity_id",
                table: "audit_log",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_contract_parts_deal_id_ord_no",
                table: "contract_parts",
                columns: new[] { "deal_id", "ord_no" });

            migrationBuilder.CreateIndex(
                name: "i_x_contract_template_parts_template_id",
                table: "contract_template_parts",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "i_x_counterparties_inn",
                table: "counterparties",
                column: "inn",
                unique: true,
                filter: "inn IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_counterparties_name",
                table: "counterparties",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "i_x_deal_items_deal_id",
                table: "deal_items",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_deal_items_product_id",
                table: "deal_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "i_x_deal_items_source_supplier_id",
                table: "deal_items",
                column: "source_supplier_id");

            migrationBuilder.CreateIndex(
                name: "i_x_deal_steps_deal_id_step",
                table: "deal_steps",
                columns: new[] { "deal_id", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_deal_steps_document_id",
                table: "deal_steps",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_deal_steps_status_due_date",
                table: "deal_steps",
                columns: new[] { "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_deals_counterparty_id",
                table: "deals",
                column: "counterparty_id");

            migrationBuilder.CreateIndex(
                name: "i_x_deals_kind_number",
                table: "deals",
                columns: new[] { "kind", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_deals_manager_id",
                table: "deals",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "i_x_deals_parent_deal_id",
                table: "deals",
                column: "parent_deal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_deals_status_created_at",
                table: "deals",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_didox_outbox_document_id",
                table: "didox_outbox",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_didox_outbox_idempotency_key",
                table: "didox_outbox",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_didox_outbox_next_try_at",
                table: "didox_outbox",
                column: "next_try_at",
                filter: "sent_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_document_files_document_id",
                table: "document_files",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_documents_counterparty_id",
                table: "documents",
                column: "counterparty_id");

            migrationBuilder.CreateIndex(
                name: "i_x_documents_deal_id",
                table: "documents",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_documents_didox_id",
                table: "documents",
                column: "didox_id",
                unique: true,
                filter: "didox_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_documents_parent_document_id",
                table: "documents",
                column: "parent_document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_documents_status_doc_type",
                table: "documents",
                columns: new[] { "status", "doc_type" });

            migrationBuilder.CreateIndex(
                name: "i_x_doverennost_info_document_id",
                table: "doverennost_info",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_doverennost_info_employee_id",
                table: "doverennost_info",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "i_x_doverennost_info_valid_until",
                table: "doverennost_info",
                column: "valid_until");

            migrationBuilder.CreateIndex(
                name: "i_x_employees_full_name",
                table: "employees",
                column: "full_name");

            migrationBuilder.CreateIndex(
                name: "i_x_integration_logs_correlation_id",
                table: "integration_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "i_x_integration_logs_document_id",
                table: "integration_logs",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_integration_logs_system_at",
                table: "integration_logs",
                columns: new[] { "system", "at" });

            migrationBuilder.CreateIndex(
                name: "i_x_notifications_dedup_key",
                table: "notifications",
                column: "dedup_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_notifications_user_id_is_read_created_at",
                table: "notifications",
                columns: new[] { "user_id", "is_read", "created_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_payments_counterparty_id_pay_date",
                table: "payments",
                columns: new[] { "counterparty_id", "pay_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_payments_deal_id",
                table: "payments",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_groups_parent_id",
                table: "product_groups",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "i_x_products_article",
                table: "products",
                column: "article");

            migrationBuilder.CreateIndex(
                name: "i_x_products_barcode",
                table: "products",
                column: "barcode");

            migrationBuilder.CreateIndex(
                name: "i_x_products_group_id",
                table: "products",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "i_x_products_name",
                table: "products",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_products_product_id",
                table: "supplier_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_products_supplier_id_product_id",
                table: "supplier_products",
                columns: new[] { "supplier_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_ttn_info_document_id",
                table: "ttn_info",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_ttn_info_driver_id",
                table: "ttn_info",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "i_x_ttn_info_vehicle_id",
                table: "ttn_info",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "i_x_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_vehicles_plate_number",
                table: "vehicles",
                column: "plate_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "company_profile");

            migrationBuilder.DropTable(
                name: "contract_parts");

            migrationBuilder.DropTable(
                name: "contract_template_parts");

            migrationBuilder.DropTable(
                name: "deal_items");

            migrationBuilder.DropTable(
                name: "deal_steps");

            migrationBuilder.DropTable(
                name: "didox_outbox");

            migrationBuilder.DropTable(
                name: "document_files");

            migrationBuilder.DropTable(
                name: "doverennost_info");

            migrationBuilder.DropTable(
                name: "integration_logs");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "number_counters");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "supplier_products");

            migrationBuilder.DropTable(
                name: "ttn_info");

            migrationBuilder.DropTable(
                name: "contract_templates");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropTable(
                name: "product_groups");

            migrationBuilder.DropTable(
                name: "deals");

            migrationBuilder.DropTable(
                name: "counterparties");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
