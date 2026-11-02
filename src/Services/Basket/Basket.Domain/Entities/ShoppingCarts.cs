namespace Basket.Domain.Entities;

public class ShoppingCart
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public List<ShoppingCartItem> Items { get; set; } = new();
    public decimal TotalPrice => Items.Sum(item => item.Price * item.Quantity);
}