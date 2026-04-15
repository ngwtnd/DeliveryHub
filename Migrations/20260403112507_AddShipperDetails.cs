using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddShipperDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF COL_LENGTH('MenuItems', 'Category') IS NULL ALTER TABLE MenuItems ADD Category nvarchar(50) NOT NULL DEFAULT '';");

            migrationBuilder.Sql("IF COL_LENGTH('AspNetUsers', 'CitizenId') IS NULL ALTER TABLE AspNetUsers ADD CitizenId nvarchar(max) NULL;");
            migrationBuilder.Sql("IF COL_LENGTH('AspNetUsers', 'CreatedAt') IS NULL ALTER TABLE AspNetUsers ADD CreatedAt datetime2 NOT NULL DEFAULT '2023-01-01T00:00:00.0000000';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "CitizenId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AspNetUsers");
        }
    }
}
