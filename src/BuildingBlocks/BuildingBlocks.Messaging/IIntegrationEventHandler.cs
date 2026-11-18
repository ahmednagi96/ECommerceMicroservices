using BuildingBlocks.Contracts;

namespace BuildingBlocks.Messaging;

public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}