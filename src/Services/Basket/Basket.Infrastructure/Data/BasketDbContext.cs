using Microsoft.EntityFrameworkCore;
using Basket.Domain.Entities;

namespace Basket.Infrastructure.Data;

public class BasketDbContext : DbContext
{
    public BasketDbContext(DbContextOptions<BasketDbContext> options) : base(options)
    {
    }
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<ShoppingCart> ShoppingCarts { get; set; } = null!;
    public DbSet<ShoppingCartItem> ShoppingCartItems { get; set; } = null!;
}