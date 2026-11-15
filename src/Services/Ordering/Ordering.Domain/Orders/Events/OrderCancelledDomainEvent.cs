
using Ordering.Domain.Common;
namespace Ordering.Domain.Orders.Events;

public sealed record OrderCancelledDomainEvent(Guid OrderId, string BuyerId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}