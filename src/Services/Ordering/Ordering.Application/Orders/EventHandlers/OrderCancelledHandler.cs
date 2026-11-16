
using Microsoft.Extensions.Logging;
using Ordering.Application.Common;
using Ordering.Domain.Orders.Events;
namespace Ordering.Application.Orders.EventHandlers;
public class OrderCancelledHandler(ILogger<OrderCancelledHandler> logger)
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public Task Handle(OrderCancelledDomainEvent e, CancellationToken ct)
    {
        logger.LogInformation("Order {OrderId} cancelled for {BuyerId}.", e.OrderId, e.BuyerId);
        // هنا بعدين: إرسال إيميل
        return Task.CompletedTask;
    }
}