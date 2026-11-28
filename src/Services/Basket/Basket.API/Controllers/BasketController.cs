using Basket.Domain.Contracts;
using Basket.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Basket.Application.DTOs.Items.Requests;
using Basket.Infrastructure.Data;
using Basket.Application.DTOs.Checkout.Requests;
using BuildingBlocks.Contracts.Basket;
using Wolverine.EntityFrameworkCore;
using Basket.Application.DTOs.Checkout.Responses;
using BuildingBlocks.Authentication;
using Microsoft.AspNetCore.Authorization;
namespace Basket.API.Controllers;


// البيانات التي نسمح للعميل بإرسالها (بدون سعر وبدون اسم لمنع التلاعب!)

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BasketController(ICatalogService catalogService,
    [FromServices] IDbContextOutbox outbox,

 BasketDbContext dbContext
//, IEventBus eventBus
 //,IMessageBus bus
, ILogger<BasketController> logger) : ControllerBase
{
    [HttpPost("items")]
    public async Task<IActionResult> AddItemToBasket(AddItemRequestDTO request, CancellationToken cancellationToken)
    {
        var BuyerId=User.GetRequiredUserId();
        CatalogProductDto? product;
        try
        {
            product = await catalogService.GetProductByIdAsync(request.ProductId, cancellationToken);
        }
       catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Catalog unavailable while adding product {ProductId}", request.ProductId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "خدمة المعرض غير متاحة حالياً، يرجى المحاولة بعد لحظات.");
        }

        // ١. نسأل خدمة المعرض لحظياً عن بيانات المنتج وسعره الحقيقي
        if (product is null)
             return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"المنتج برقم {request.ProductId} غير موجود في المعرض.");
        // ٢. نبحث في قاعدة بيانات السلة: هل العميل لديه سلة حالية؟
        var cart = await dbContext.ShoppingCarts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.BuyerId == BuyerId, cancellationToken);
        // إذا لم يكن لديه سلة، ننشئ له سلة أب جديدة
        if (cart is null)
        {
            cart = new ShoppingCart { BuyerId = BuyerId };
            dbContext.ShoppingCarts.Add(cart);
        }
        // ٣. نفحص هل المنتج موجود مسبقاً داخل سلة هذا العميل أم لا؟
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
        if (existingItem is not null)
        {
            // إذا كان موجوداً، نزيد الكمية فقط ونحدث السعر
            existingItem.Quantity += request.Quantity;
            // Only overwrite if Catalog gave us a newer price
            if (product.PriceVersion > existingItem.PriceVersion)
            {
                existingItem.Price = product.Price;
                existingItem.PriceVersion = product.PriceVersion;
            }
        }
        else
        {
            // إذا لم يكن موجوداً، ننشئ عنصر سلة جديداً ونضعه داخل قائمة السلة الأب
            cart.Items.Add(new ShoppingCartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Price = product.Price,
                PriceVersion = product.PriceVersion,   // NEW
                Quantity = request.Quantity
            });
        }
        // ٤. نحفظ التعديلات في قاعدة بيانات السلة، ونرجع السلة كاملة بإجمالي سعرها
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(cart);
    }



    [HttpPost("checkout")]
public async Task<IActionResult> Checkout(CheckoutRequestDTO request, CancellationToken cancellationToken)
{
    var BuyerId=User.GetRequiredUserId();
    // 1) هات السلة
    var cart = await dbContext.ShoppingCarts
        .Include(c => c.Items)
        .FirstOrDefaultAsync(c => c.BuyerId == BuyerId, cancellationToken);

    if (cart is null || cart.Items.Count == 0)
        return Problem(statusCode: StatusCodes.Status400BadRequest, title: "السلة فاضية.");

        IReadOnlyList<CatalogProductDto> catalogPrices;
        try
        {
            var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
            catalogPrices = await catalogService.GetPricesAsync(productIds, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Catalog unavailable during checkout of {UserName}", cart.BuyerId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "مش قادرين نتأكد من الأسعار دلوقتي، حاول بعد لحظات.");
        }
        //  NEW: compare the cart with the truth
            var currentPrices = catalogPrices.ToDictionary(p => p.Id);
            var priceChanges = new List<PriceChangeDTO>();
            var removedItems = new List<ShoppingCartItem>();
            foreach (var item in cart.Items)
            {
                if (!currentPrices.TryGetValue(item.ProductId, out var current))
                {
                    removedItems.Add(item);   // product is gone from Catalog
                    continue;
                }

                if (current.Price != item.Price)
                {
                    priceChanges.Add(new PriceChangeDTO(item.ProductId, item.ProductName, item.Price, current.Price));
                    item.Price = current.Price;              // fix the cart with the real price
                    item.PriceVersion = current.PriceVersion;
                }
            }


            // 4) NEW: something changed → fix the cart, stop, tell the customer
        if (priceChanges.Count > 0 || removedItems.Count > 0)
        {
            dbContext.ShoppingCartItems.RemoveRange(removedItems);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Conflict(new CheckoutConflictResponseDTO(
                Message: "بعض المنتجات في سلتك اتغيرت. راجع السلة ودوس Checkout تاني.",
                PriceChanges: priceChanges,
                RemovedProducts: removedItems.Select(i => i.ProductName).ToList()));
        }


    // 2) حوّل السلة للعقد
    var checkoutEvent = new BasketCheckedOutIntegrationEvent(
        BuyerId: cart.BuyerId,
        Currency: "EGP",
        ShippingAddress: new CheckoutAddress(request.Street, request.City, request.Country, request.ZipCode),
        Items: cart.Items
            .Select(i => new CheckoutItem(i.ProductId, i.ProductName, i.Price, i.Quantity))
            .ToList());

      // 3) Use OUR DbContext, so everything is in one transaction
    outbox.Enroll(dbContext);

// 4) Nothing is sent yet: Wolverine only keeps the message in memory
    await outbox.PublishAsync(checkoutEvent);
    // 5) امسح السلة (والمنتجات اللي جواها)
    dbContext.ShoppingCartItems.RemoveRange(cart.Items);
    dbContext.ShoppingCarts.Remove(cart);
        // 6) ONE transaction (basket delete + message row), THEN send to RabbitMQ
    await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

    // 5) "استلمنا طلبك" ✅
    logger.LogInformation("Basket of {UserName} checked out. CheckoutId: {CheckoutId}", cart.BuyerId, checkoutEvent.Id);
    return Accepted(new { checkoutId = checkoutEvent.Id });
}
}