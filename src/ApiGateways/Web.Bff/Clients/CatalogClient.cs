namespace Web.Bff.Clients;

public sealed class CatalogClient(HttpClient http)
{
    public async Task<IReadOnlyList<ProductSummaryDto>> GetProductsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new List<ProductSummaryDto>();

        // Catalog accepts max 100 ids per call
        foreach (var chunk in ids.Chunk(100))
        {
            var query = string.Join("&", chunk.Select(id => $"ids={id}"));
            var page = await http.GetFromJsonAsync<List<ProductSummaryDto>>($"api/products?{query}", ct);
            result.AddRange(page ?? []);
        }

        return result;
    }
}