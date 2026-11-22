using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Catalog.Infrastructure.Data;
using Wolverine.EntityFrameworkCore;

using Catalog.Application.DTOs.Products.Requests;
using Catalog.Domain.Exceptions;
using BuildingBlocks.Contracts.Catalog;
using Catalog.Application.DTOs.Products.Responses;
using Microsoft.Extensions.Caching.Hybrid;
using Catalog.Application.Caching;
using Catalog.Application.Queries;
namespace Catalog.API.Controllers;
[ApiController]
[Route("api/[controller]")]

public class ProductsController(
    CatalogDbContext dbContext,
    HybridCache cache,
    ICatalogQueries catalogQueries,
    [FromServices] IDbContextOutbox outbox,
    ILogger<ProductsController> logger
    ) : ControllerBase
{

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken cancellationToken)
    {
        var product = await catalogQueries.GetProductByIdAsync(id, cancellationToken);

        if (product is null)
            return NotFound($"المنتج برقم {id} غير موجود في المعرض.");

        return Ok(product);
    }


     [HttpPut("{id:guid}/price")]
    public async Task<IActionResult> ChangePrice(
        Guid id, ChangePriceRequest request, CancellationToken cancellationToken)
    {
        // 1) Load the product
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
            return NotFound($"المنتج برقم {id} غير موجود في المعرض.");

        // 2) Let the product change its own price
        bool changed;
        try
        {
            changed = product.ChangePrice(request.NewPrice);
        }
        catch (DomainException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        // 3) Same price: no save, no event
        if (!changed)
            return Ok(new { product.Id, product.Price, product.PriceVersion });

        // 4) Join the outbox to our DbContext, then queue the event (not sent yet)
        outbox.Enroll(dbContext);
        await outbox.PublishAsync(new ProductPriceChangedIntegrationEvent(
            ProductId: product.Id,
            NewPrice: product.Price,
            Version: product.PriceVersion));

        // 5) ONE transaction: new price + event row. Then send to RabbitMQ
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "السعر اتغير من حد تاني. اعمل Refresh وحاول تاني.");
        }

        // NEW: the price is saved → drop the old copy from the cache
        await cache.RemoveAsync(CatalogCacheKeys.Product(id), cancellationToken);
        logger.LogInformation(
            "Price of {ProductId} changed to {Price} (v{Version})",
            product.Id, product.Price, product.PriceVersion);

        return Ok(new { product.Id, product.Price, product.PriceVersion });
    }
    
        [HttpGet("prices")]
        public async Task<IActionResult> GetPrices(
            [FromQuery] Guid[] ids, CancellationToken cancellationToken)
        {
            // Protect Catalog from empty or huge requests
            if (ids.Length is 0 or > 100)
                return Problem(statusCode: StatusCodes.Status400BadRequest,
                            title: "ids must contain between 1 and 100 items.");

            var prices = await dbContext.Products
                .AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new ProductResponse(p.Id, p.Name, p.Price, p.PriceVersion))
                .ToListAsync(cancellationToken);

            return Ok(prices);
        }
}