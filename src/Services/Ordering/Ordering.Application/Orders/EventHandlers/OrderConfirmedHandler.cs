
using Microsoft.Extensions.Logging;
using Ordering.Application.Common;
using Ordering.Domain.Orders.Events;
namespace Ordering.Application.Orders.EventHandlers;
public class OrderConfirmedHandler(ILogger<OrderConfirmedHandler> logger)
    : IDomainEventHandler<OrderConfirmedDomainEvent>
{
    public Task Handle(OrderConfirmedDomainEvent e, CancellationToken ct)
    {
        logger.LogInformation("Order {OrderId} confirmed for {BuyerId}. Total: {Total}",
            e.OrderId, e.BuyerId, e.Total);
        // هنا بعدين: إرسال إيميل
        return Task.CompletedTask;
    }
}