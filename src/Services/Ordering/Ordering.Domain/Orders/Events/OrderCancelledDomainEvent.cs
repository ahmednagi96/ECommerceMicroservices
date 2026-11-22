
using Ordering.Domain.Common;
using Ordering.Domain.ValueObjects;
namespace Ordering.Domain.Orders.Events;

public sealed record OrderCancelledDomainEvent(OrderId OrderId, string BuyerId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}