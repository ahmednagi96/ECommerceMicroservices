using System.Net;
using System.Net.Http.Json;
using Basket.Domain.Contracts;

namespace Basket.Infrastructure.ExternalServices.Catalog;

public class CatalogService(HttpClient httpClient) : ICatalogService
{
    public async Task<CatalogProductDto?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        // إرسال طلب قراءة إلى نقطة النهاية في خدمة المعرض
        var response = await httpClient.GetAsync($"/api/products/{productId}", cancellationToken);

        // إذا كان المنتج غير موجود في المعرض (404)، نرجع قيمة فارغة بهدوء
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // التأكد من نجاح الاتصال (يرمي خطأ واضحاً لو كان خادم المعرض متوقفاً)
        response.EnsureSuccessStatusCode();

        // تحويل رد الجيسون مباشرة إلى كائن سي شارب
        return await response.Content.ReadFromJsonAsync<CatalogProductDto>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogProductDto>> GetPricesAsync(
    IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
        {
            // ids=a&ids=b&ids=c
            var query = string.Join("&", productIds.Select(id => $"ids={id}"));

            var response = await httpClient.GetAsync($"/api/products/prices?{query}", cancellationToken);
            response.EnsureSuccessStatusCode();   // Catalog down or error → exception

            return await response.Content.ReadFromJsonAsync<List<CatalogProductDto>>(cancellationToken: cancellationToken)
                ?? [];
        }
}