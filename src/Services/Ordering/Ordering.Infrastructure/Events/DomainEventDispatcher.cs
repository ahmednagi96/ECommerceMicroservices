
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Common;
using Ordering.Application.Common;
namespace Ordering.Infrastructure.Events;
public class DomainEventDispatcher(IServiceProvider serviceProvider)
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;

            foreach (var handler in serviceProvider.GetServices(handlerType))
            {
                if (handler is null) continue;
                await (Task)handleMethod.Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}