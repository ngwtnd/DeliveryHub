using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTiersAndLocking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedOrdersCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LastVoucherMonth",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyFailedOrdersCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PendingLock",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSpent",
                table: "AspNetUsers",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "UserTier",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedOrdersCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastVoucherMonth",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MonthlyFailedOrdersCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PendingLock",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TotalSpent",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UserTier",
                table: "AspNetUsers");
        }
    }
}
