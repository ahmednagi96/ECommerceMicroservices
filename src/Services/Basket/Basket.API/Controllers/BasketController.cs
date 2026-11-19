using Basket.Domain.Contracts;
using Basket.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Basket.Application.DTOs.Items.Requests;
using Basket.Infrastructure.Data;
using BuildingBlocks.Messaging;
using Basket.Application.DTOs.Checkout.Requests;
using BuildingBlocks.Contracts.Basket;
using Basket.Infrastructure.Outbox;
using Wolverine;
namespace Basket.API.Controllers;


// البيانات التي نسمح للعميل بإرسالها (بدون سعر وبدون اسم لمنع التلاعب!)

[ApiController]
[Route("api/[controller]")]
public class BasketController(ICatalogService catalogService, BasketDbContext dbContext
//, IEventBus eventBus
, ILogger<BasketController> logger) : ControllerBase
{
    [HttpPost("items")]
    public async Task<IActionResult> AddItemToBasket(AddItemRequestDTO request, CancellationToken cancellationToken)
    {
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
            .FirstOrDefaultAsync(c => c.UserName == request.UserName, cancellationToken);
        // إذا لم يكن لديه سلة، ننشئ له سلة أب جديدة
        if (cart is null)
        {
            cart = new ShoppingCart { UserName = request.UserName };
            dbContext.ShoppingCarts.Add(cart);
        }
        // ٣. نفحص هل المنتج موجود مسبقاً داخل سلة هذا العميل أم لا؟
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
        if (existingItem is not null)
        {
            // إذا كان موجوداً، نزيد الكمية فقط ونحدث السعر
            existingItem.Quantity += request.Quantity;
            existingItem.Price = product.Price;
        }
        else
        {
            // إذا لم يكن موجوداً، ننشئ عنصر سلة جديداً ونضعه داخل قائمة السلة الأب
            cart.Items.Add(new ShoppingCartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Price = product.Price,
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
    // 1) هات السلة
    var cart = await dbContext.ShoppingCarts
        .Include(c => c.Items)
        .FirstOrDefaultAsync(c => c.UserName == request.UserName, cancellationToken);

    if (cart is null || cart.Items.Count == 0)
        return Problem(statusCode: StatusCodes.Status400BadRequest, title: "السلة فاضية.");

    // 2) حوّل السلة للعقد
    var checkoutEvent = new BasketCheckedOutIntegrationEvent(
        BuyerId: cart.UserName,
        Currency: "EGP",
        ShippingAddress: new CheckoutAddress(request.Street, request.City, request.Country, request.ZipCode),
        Items: cart.Items
            .Select(i => new CheckoutItem(i.ProductId, i.ProductName, i.Price, i.Quantity))
            .ToList());

   // 3) Don't publish now: put the message in the Outbox table
  dbContext.OutboxMessages.Add(
    OutboxMessage.Create(checkoutEvent.Id, checkoutEvent.OccurredOn, checkoutEvent));


    // 4) امسح السلة (والمنتجات اللي جواها)
    dbContext.ShoppingCartItems.RemoveRange(cart.Items);
    dbContext.ShoppingCarts.Remove(cart);
    await dbContext.SaveChangesAsync(cancellationToken);

    // 5) "استلمنا طلبك" ✅
    logger.LogInformation("Basket of {UserName} checked out. CheckoutId: {CheckoutId}", cart.UserName, checkoutEvent.Id);
    return Accepted(new { checkoutId = checkoutEvent.Id });
}
}