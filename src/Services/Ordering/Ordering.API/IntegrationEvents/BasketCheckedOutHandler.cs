using BuildingBlocks.Contracts.Basket;
using BuildingBlocks.Messaging;
using Ordering.Domain.Orders;
using Ordering.Domain.ValueObjects;

namespace Ordering.API.IntegrationEvents;

public sealed class BasketCheckedOutHandler(
    IOrderRepository orderRepository,
    ILogger<BasketCheckedOutHandler> logger)
   // : IIntegrationEventHandler<BasketCheckedOutIntegrationEvent>
{
//    public async Task HandleAsync(BasketCheckedOutIntegrationEvent e, CancellationToken ct)
      public async Task Handle(BasketCheckedOutIntegrationEvent e, CancellationToken ct)

    {
        // 1) العقد ← الدومين بتاعنا
        var address = Address.Create(
            e.ShippingAddress.Street, e.ShippingAddress.City,
            e.ShippingAddress.Country, e.ShippingAddress.ZipCode);

        var order = Order.Create(e.BuyerId, address, e.Currency);

        foreach (var item in e.Items)
            order.AddItem(item.ProductId, item.ProductName, Money.Of(item.UnitPrice, e.Currency), item.Quantity);

        // 2) احفظ
        orderRepository.Add(order);
        await orderRepository.SaveChangesAsync(ct);

        logger.LogInformation("📦 Order {OrderId} created from checkout {CheckoutId}", order.Id, e.Id);
    }
}