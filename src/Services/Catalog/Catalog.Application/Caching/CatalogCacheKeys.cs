namespace Catalog.Application.Caching;

// One place for cache keys: the query writes with it, the price change removes with it
public static class CatalogCacheKeys
{
    public static string Product(Guid id) => $"product:{id}";
}
