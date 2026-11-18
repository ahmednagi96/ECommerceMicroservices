using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basket.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// SQL Server can't convert int -> uniqueidentifier with ALTER COLUMN,
    /// so we drop and re-add the column. Old int ProductIds have no Guid
    /// equivalent, so existing rows get Guid.Empty.
    public partial class ConvertProductIdToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ProductId", table: "ShoppingCartItems");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "ShoppingCartItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: Guid.Empty);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ProductId", table: "ShoppingCartItems");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "ShoppingCartItems",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
