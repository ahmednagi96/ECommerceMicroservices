using Microsoft.EntityFrameworkCore;
using Catalog.Domain.Entities;


namespace Catalog.Infrastructure.Data;

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}