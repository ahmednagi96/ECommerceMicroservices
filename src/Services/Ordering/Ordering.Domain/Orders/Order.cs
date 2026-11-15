using Ordering.Domain.Exceptions;
using Ordering.Domain.ValueObjects;
using Ordering.Domain.Common;
using Ordering.Domain.Orders.Events;
namespace Ordering.Domain.Orders;

public class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = new();

    public OrderId Id { get; private set; }
    public string BuyerId { get; private set; } = default!;
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string Currency { get; private set; } = default!;
    public Address ShippingAddress { get; private set; } = default!;


    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
   public Money Total =>
    _items.Aggregate(Money.Zero(Currency), (sum, item) => sum.Add(item.LineTotal));

    private Order() { } // EF Core

    public static Order Create(string buyerId, Address shippingAddress, string currency)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new DomainException("BuyerId is required.");

         return new Order
        {
            Id = OrderId.New(),
            BuyerId = buyerId,
            ShippingAddress = shippingAddress,
            Currency = currency,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddItem(Guid productId, string name, Money unitPrice, int quantity)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot modify a non-pending order.");
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");
        if (unitPrice.Currency != Currency)
        throw new DomainException("Item currency must match order currency.");

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
        
        Raise(new OrderConfirmedDomainEvent(Id, BuyerId, Total)); // 📢

    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            return; // ملغي أصلًا، مفيش حاجة جديدة حصلت، فمفيش حدث

        if (Status == OrderStatus.Shipped)
            throw new DomainException("Shipped orders cannot be cancelled.");

        Status = OrderStatus.Cancelled;
        Raise(new OrderCancelledDomainEvent(Id, BuyerId));
    }

    public void RemoveItem(Guid productId)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot modify a non-pending order.");

        var existing = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new DomainException("Item not found in the order.");

        _items.Remove(existing);
    }

   public void UpdateShippingAddress(Address newAddress)
    {
        ArgumentNullException.ThrowIfNull(newAddress);

        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot modify a non-pending order.");

        ShippingAddress = newAddress;
    }
}