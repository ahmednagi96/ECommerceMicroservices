namespace Basket.Domain.Entities;
public class ShoppingCartItem
{
    public int Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public long PriceVersion { get; set; }   // NEW: which Catalog version this price came from
    public int Quantity { get; set; }
}