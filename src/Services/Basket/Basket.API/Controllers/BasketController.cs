using Basket.Domain.Contracts;
using Basket.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Basket.Application.DTOs.Items.Requests;
using Basket.Infrastructure.Data;
namespace Basket.API.Controllers;


// البيانات التي نسمح للعميل بإرسالها (بدون سعر وبدون اسم لمنع التلاعب!)

[ApiController]
[Route("api/[controller]")]
public class BasketController(ICatalogService catalogService, BasketDbContext dbContext) : ControllerBase
{
    [HttpPost("items")]
    public async Task<IActionResult> AddItemToBasket(AddItemRequestDTO request, CancellationToken cancellationToken)
    {
        CatalogProductDto? product;
        try
        {
            product = await catalogService.GetProductByIdAsync(request.ProductId, cancellationToken);
        }
        catch (Exception)
        {
            return StatusCode(503, "خدمة المعرض غير متاحة حالياً للتحقق من سعر المنتج، يرجى المحاولة بعد لحظات.");
        }
        // ١. نسأل خدمة المعرض لحظياً عن بيانات المنتج وسعره الحقيقي
        if (product is null)
            return NotFound($"المنتج برقم {request.ProductId} غير موجود في المعرض.");
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
}