using Microsoft.EntityFrameworkCore;
using Catalog.Domain.Catalog;


namespace Catalog.Infrastructure.Data;

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }
    public DbSet<Product> Products { get; set; } = null!;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>()
            .Property(p => p.PriceVersion)
            .IsConcurrencyToken();

        modelBuilder.Entity<Product>()
            .Property(p => p.ImageUrl)
            .IsRequired(false);
    }
}