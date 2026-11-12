using Ordering.Domain.Exceptions;
namespace Ordering.Domain.Orders;

public class Order
{
    private readonly List<OrderItem> _items = new();

    public Guid Id { get; private set; }
    public string BuyerId { get; private set; } = default!;
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public decimal Total => _items.Sum(i => i.UnitPrice * i.Quantity);

    private Order() { } // EF Core

    public static Order Create(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new DomainException("BuyerId is required.");

        return new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddItem(Guid productId, string name, decimal unitPrice, int quantity)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot modify a non-pending order.");
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");
        if (unitPrice < 0)
            throw new DomainException("Price cannot be negative.");

        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
            existing.IncreaseQuantity(quantity);
        else
            _items.Add(new OrderItem(productId, name, unitPrice, quantity));
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only pending orders can be confirmed.");
        if (_items.Count == 0)
            throw new DomainException("Cannot confirm an empty order.");

        Status = OrderStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Shipped)
            throw new DomainException("Shipped orders cannot be cancelled.");

        Status = OrderStatus.Cancelled;
    }

    public void RemoveItem(Guid productId)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot modify a non-pending order.");

        var existing = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new DomainException("Item not found in the order.");

        _items.Remove(existing);
    }
}