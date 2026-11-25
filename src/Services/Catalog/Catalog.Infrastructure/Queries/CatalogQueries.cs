using Catalog.Application.Caching;
using Catalog.Application.DTOs.Products.Responses;
using Catalog.Application.Queries;
using Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Catalog.Infrastructure.Queries;

public sealed class CatalogQueries(CatalogDbContext dbContext, HybridCache cache) : ICatalogQueries
{
    public async Task<ProductDetailsResponse?> GetProductByIdAsync(Guid id, CancellationToken ct)
    {
        // Found in cache → return it. Not found → run the query, cache it, return it
        return await cache.GetOrCreateAsync(
            CatalogCacheKeys.Product(id),
            async token => await dbContext.Products
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new ProductDetailsResponse(
                    p.Id, p.Name, p.Description, p.Price, p.PriceVersion, p.ImageUrl))
                .FirstOrDefaultAsync(token),
            cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ProductSummaryResponse>> GetProductsSummaryByIdAsync(Guid[] ids, CancellationToken ct)
    {
        return await dbContext.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProductSummaryResponse(p.Id, p.Name, p.ImageUrl, p.Price, p.Stock > 0))
            .ToListAsync(ct);
    }
}
