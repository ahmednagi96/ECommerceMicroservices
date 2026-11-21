using Basket.Infrastructure.Data;
using BuildingBlocks.Contracts.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Basket.API.IntegrationEvents;

public sealed class ProductPriceChangedHandler(
    BasketDbContext dbContext,
    ILogger<ProductPriceChangedHandler> logger)
{
    public async Task Handle(ProductPriceChangedIntegrationEvent e, CancellationToken ct)
    {
        // One SQL UPDATE: only this product, only if our version is older
        var updatedItems = await dbContext.ShoppingCartItems
            .Where(i => i.ProductId == e.ProductId && i.PriceVersion < e.Version)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Price, e.NewPrice)
                .SetProperty(i => i.PriceVersion, e.Version), ct);

        logger.LogInformation(
            "Price of {ProductId} → {Price} (v{Version}). Updated {Count} cart items",
            e.ProductId, e.NewPrice, e.Version, updatedItems);
    }
}