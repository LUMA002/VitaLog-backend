using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VitaLog.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateProductDeleteBehaviorToCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Users_CreatorUserId",
                table: "Products");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Users_CreatorUserId",
                table: "Products",
                column: "CreatorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Users_CreatorUserId",
                table: "Products");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Users_CreatorUserId",
                table: "Products",
                column: "CreatorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
