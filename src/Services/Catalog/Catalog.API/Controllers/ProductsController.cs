using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Catalog.Infrastructure.Data;
using Wolverine.EntityFrameworkCore;

using Catalog.Application.DTOs.Products.Requests;
using Catalog.Domain.Exceptions;
using BuildingBlocks.Contracts.Catalog;
namespace Catalog.API.Controllers;
[ApiController]
[Route("api/[controller]")]

public class ProductsController(
    CatalogDbContext dbContext,
    [FromServices] IDbContextOutbox outbox,
    ILogger<ProductsController> logger
    ) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
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

        logger.LogInformation(
            "Price of {ProductId} changed to {Price} (v{Version})",
            product.Id, product.Price, product.PriceVersion);

        return Ok(new { product.Id, product.Price, product.PriceVersion });
    }
}