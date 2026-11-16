
using Ordering.Domain.Common;
using Ordering.Domain.ValueObjects;
using Ordering.Domain.Orders;
namespace Ordering.Domain.Orders.Events;
public sealed record OrderConfirmedDomainEvent(OrderId OrderId, string BuyerId, Money Total)
    : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}