using BuildingBlocks.Contracts.Basket;
using Ordering.Domain.Orders;
using Ordering.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

        var order = Order.Create(e.BuyerId, address, e.Currency, checkoutId: e.Id);

        foreach (var item in e.Items)
            order.AddItem(item.ProductId, item.ProductName, Money.Of(item.UnitPrice, e.Currency), item.Quantity);

        // 2) احفظ
        orderRepository.Add(order);
        try
            {
                await orderRepository.SaveChangesAsync(ct);
            }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Same checkout already created an order: this is a duplicate message
                logger.LogWarning("Duplicate checkout {CheckoutId} ignored", e.Id);
                return;
            }

        logger.LogInformation("📦 Order {OrderId} created from checkout {CheckoutId}", order.Id, e.Id);
    }
}