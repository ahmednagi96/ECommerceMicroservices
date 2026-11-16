using Ordering.Domain.ValueObjects;
namespace Ordering.Domain.Orders;
public class OrderItem
{
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = default!;
    public Money UnitPrice { get; private set; } = default!;
    public int Quantity { get; private set; }

    private OrderItem() { } // EF Core

    internal OrderItem(Guid productId, string productName, Money unitPrice, int quantity)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void IncreaseQuantity(int amount) => Quantity += amount;

    public Money LineTotal => UnitPrice.Multiply(Quantity);
}