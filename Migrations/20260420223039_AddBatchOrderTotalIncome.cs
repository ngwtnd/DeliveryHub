using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchOrderTotalIncome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalIncome",
                table: "BatchOrders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalIncome",
                table: "BatchOrders");
        }
    }
}
