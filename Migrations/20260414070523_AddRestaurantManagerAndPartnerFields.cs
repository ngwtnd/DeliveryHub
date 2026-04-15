using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantManagerAndPartnerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActivityState",
                table: "Stores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CommentForShipper",
                table: "Reviews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManagedStoreId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BatchOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShipperId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalDistance = table.Column<double>(type: "float", nullable: false),
                    EstimatedMinutes = table.Column<double>(type: "float", nullable: false),
                    DeliveryAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeliveryLatitude = table.Column<double>(type: "float", nullable: false),
                    DeliveryLongitude = table.Column<double>(type: "float", nullable: false),
                    RouteGeometry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OptimizedRouteJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchOrders_AspNetUsers_ShipperId",
                        column: x => x.ShipperId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BatchOrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchOrderId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    IsPickedUp = table.Column<bool>(type: "bit", nullable: false),
                    PickedUpAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchOrderItems_BatchOrders_BatchOrderId",
                        column: x => x.BatchOrderId,
                        principalTable: "BatchOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BatchOrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchOrderItems_BatchOrderId",
                table: "BatchOrderItems",
                column: "BatchOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchOrderItems_OrderId",
                table: "BatchOrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchOrders_BatchCode",
                table: "BatchOrders",
                column: "BatchCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchOrders_ShipperId",
                table: "BatchOrders",
                column: "ShipperId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchOrderItems");

            migrationBuilder.DropTable(
                name: "BatchOrders");

            migrationBuilder.DropColumn(
                name: "ActivityState",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "CommentForShipper",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ManagedStoreId",
                table: "AspNetUsers");
        }
    }
}
