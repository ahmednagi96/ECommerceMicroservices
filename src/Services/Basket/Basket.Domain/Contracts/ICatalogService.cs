namespace Basket.Domain.Contracts;

// ١. شكل البيانات التي سنستقبلها من خدمة المعرض (نأخذ ما يهم السلة فقط)
public record CatalogProductDto(int Id, string Name, decimal Price);

// ٢. العقد البرمجي الذي ستستخدمه السلة لطلب بيانات المنتج
public interface ICatalogService
{
    Task<CatalogProductDto?> GetProductByIdAsync(int productId, CancellationToken cancellationToken = default);
}