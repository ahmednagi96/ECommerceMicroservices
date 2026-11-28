using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basket.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EditUserNameFieldToBuyerIdInOrdersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UserName",
                table: "ShoppingCarts",
                newName: "BuyerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BuyerId",
                table: "ShoppingCarts",
                newName: "UserName");
        }
    }
}
