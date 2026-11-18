using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// SQL Server can't ALTER an IDENTITY int column to uniqueidentifier,
    /// so we drop the PK + column and recreate it as a Guid.
    /// Existing products get a fresh NEWID() value.
    public partial class ConvertProductIdToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_Products", table: "Products");
            migrationBuilder.DropColumn(name: "Id", table: "Products");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "Products",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddPrimaryKey(name: "PK_Products", table: "Products", column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_Products", table: "Products");
            migrationBuilder.DropColumn(name: "Id", table: "Products");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "Products",
                type: "int",
                nullable: false)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(name: "PK_Products", table: "Products", column: "Id");
        }
    }
}
