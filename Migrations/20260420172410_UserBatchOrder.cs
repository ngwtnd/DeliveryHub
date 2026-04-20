using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class UserBatchOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ShipperId",
                table: "BatchOrders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "BatchOrders",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchOrders_UserId",
                table: "BatchOrders",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BatchOrders_AspNetUsers_UserId",
                table: "BatchOrders",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BatchOrders_AspNetUsers_UserId",
                table: "BatchOrders");

            migrationBuilder.DropIndex(
                name: "IX_BatchOrders_UserId",
                table: "BatchOrders");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "BatchOrders");

            migrationBuilder.AlterColumn<string>(
                name: "ShipperId",
                table: "BatchOrders",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
