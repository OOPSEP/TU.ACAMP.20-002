using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TU.ACAMP.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddGhgTargetAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TargetReductionPercent",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.AddColumn<string>(
                name: "AlertEmails",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "AlertThresholdPercent",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "EnableAlert",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LastAlertRatio",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAlertSentAt",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastAlertYear",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tb_organization_ghg_target_alert_log",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    CorporateId = table.Column<int>(type: "int", nullable: true),
                    ReportingYear = table.Column<int>(type: "int", nullable: false),
                    CurrentEmissionsTon = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TargetCapTon = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConsumptionPercent = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    RecipientEmails = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlertTier = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ErrorMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_organization_ghg_target_alert_log", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tb_organization_ghg_target_alert_log_tb_organization_corpora~",
                        column: x => x.CorporateId,
                        principalSchema: "organization",
                        principalTable: "tb_organization_corporate",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_tb_organization_ghg_target_alert_log_tb_subscription_Subscri~",
                        column: x => x.SubscriptionId,
                        principalTable: "tb_subscription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_alert_sub_year_tier",
                schema: "organization",
                table: "tb_organization_ghg_target_alert_log",
                columns: new[] { "SubscriptionId", "ReportingYear", "AlertTier" });

            migrationBuilder.CreateIndex(
                name: "IX_tb_organization_ghg_target_alert_log_CorporateId",
                schema: "organization",
                table: "tb_organization_ghg_target_alert_log",
                column: "CorporateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_organization_ghg_target_alert_log",
                schema: "organization");

            migrationBuilder.DropColumn(
                name: "AlertEmails",
                schema: "organization",
                table: "tb_organization_netzero_target");

            migrationBuilder.DropColumn(
                name: "AlertThresholdPercent",
                schema: "organization",
                table: "tb_organization_netzero_target");

            migrationBuilder.DropColumn(
                name: "EnableAlert",
                schema: "organization",
                table: "tb_organization_netzero_target");

            migrationBuilder.DropColumn(
                name: "LastAlertRatio",
                schema: "organization",
                table: "tb_organization_netzero_target");

            migrationBuilder.DropColumn(
                name: "LastAlertSentAt",
                schema: "organization",
                table: "tb_organization_netzero_target");

            migrationBuilder.DropColumn(
                name: "LastAlertYear",
                schema: "organization",
                table: "tb_organization_netzero_target");

            migrationBuilder.AlterColumn<decimal>(
                name: "TargetReductionPercent",
                schema: "organization",
                table: "tb_organization_netzero_target",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2);
        }
    }
}
