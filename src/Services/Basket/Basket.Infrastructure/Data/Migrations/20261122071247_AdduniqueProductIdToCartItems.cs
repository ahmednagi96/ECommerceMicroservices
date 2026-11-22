using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basket.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdduniqueProductIdToCartItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShoppingCartItems_ShoppingCartId",
                table: "ShoppingCartItems");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingCartItems_ShoppingCartId_ProductId",
                table: "ShoppingCartItems",
                columns: new[] { "ShoppingCartId", "ProductId" },
                unique: true,
                filter: "[ShoppingCartId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShoppingCartItems_ShoppingCartId_ProductId",
                table: "ShoppingCartItems");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingCartItems_ShoppingCartId",
                table: "ShoppingCartItems",
                column: "ShoppingCartId");
        }
    }
}
