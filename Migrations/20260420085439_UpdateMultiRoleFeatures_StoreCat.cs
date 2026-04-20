using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMultiRoleFeatures_StoreCat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreCategory",
                table: "Stores",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RatingStoreByShipper",
                table: "Reviews",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrentShipperOfferedId",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OfferExpiresAt",
                table: "Orders",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreCategory",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "RatingStoreByShipper",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "CurrentShipperOfferedId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OfferExpiresAt",
                table: "Orders");
        }
    }
}
