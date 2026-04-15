using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceAndShipperIncomeToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServiceId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShipperIncome",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ServiceType",
                table: "DeliveryServices",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ServiceId",
                table: "Orders",
                column: "ServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_DeliveryServices_ServiceId",
                table: "Orders",
                column: "ServiceId",
                principalTable: "DeliveryServices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_DeliveryServices_ServiceId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ServiceId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ServiceId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipperIncome",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ServiceType",
                table: "DeliveryServices");
        }
    }
}
