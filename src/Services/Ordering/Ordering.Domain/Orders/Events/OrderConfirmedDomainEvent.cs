
using Ordering.Domain.Common;
using Ordering.Domain.ValueObjects;
namespace Ordering.Domain.Orders.Events;
public sealed record OrderConfirmedDomainEvent(Guid OrderId, string BuyerId, Money Total)
    : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}