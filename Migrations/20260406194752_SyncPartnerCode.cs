using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryHubWeb.Migrations
{
    /// <inheritdoc />
    public partial class SyncPartnerCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adding PartnerCode column. Other columns omitted as they were already applied manually.
            try
            {
                migrationBuilder.AddColumn<string>(
                    name: "PartnerCode",
                    table: "AspNetUsers",
                    type: "nvarchar(max)",
                    nullable: true);
            }
            catch { }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartnerCode",
                table: "AspNetUsers");
        }
    }
}
