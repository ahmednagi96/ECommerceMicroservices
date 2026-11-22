using Catalog.Application.DTOs.Products.Responses;

namespace Catalog.Application.Queries;

public interface ICatalogQueries
{
    // Returns null when the product does not exist
    Task<ProductDetailsResponse?> GetProductByIdAsync(Guid id, CancellationToken ct);
}
