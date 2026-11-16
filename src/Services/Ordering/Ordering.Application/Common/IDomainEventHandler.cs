using Ordering.Domain.Common;
namespace Ordering.Application.Common;
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}