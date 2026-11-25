using Web.Bff.Clients;

namespace Web.Bff.MyOrders;

public static class MyOrdersEndpoint
{
    public static void MapMyOrders(this WebApplication app) =>
        app.MapGet("/my-orders", GetMyOrders);

    private static async Task<IResult> GetMyOrders(
        string buyerId,
        OrderingClient ordering,
        CatalogClient catalog,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("MyOrders");

        if (string.IsNullOrWhiteSpace(buyerId))
            return TypedResults.Problem(statusCode: 400, title: "buyerId is required.");

        // 1) Orders are the core of the page: no orders = no page
        IReadOnlyList<OrderDto> orders;
        try
        {
            orders = await ordering.GetOrdersAsync(buyerId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ordering unavailable for buyer {BuyerId}", buyerId);
            return TypedResults.Problem(statusCode: 503,
                title: "Orders are unavailable right now. Please try again.");
        }

        if (orders.Count == 0)
            return TypedResults.Ok(new MyOrdersResponse(true, []));

        // 2) Product details are an enhancement: if Catalog fails, keep going
        var productIds = orders
            .SelectMany(o => o.Items)
            .Select(i => i.ProductId)
            .Distinct()
            .ToArray();

        Dictionary<Guid, ProductSummaryDto>? products = null;
        try
        {
            var list = await catalog.GetProductsAsync(productIds, ct);
            products = list.ToDictionary(p => p.Id);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Catalog unavailable, returning orders without product details");
        }

        // 3) Merge
        var views = orders.Select(o => new MyOrderView(
            o.Id, o.Status, o.CreatedAt, o.Currency, o.Total,
            o.Items.Select(i => ToItemView(i, products)).ToList()))
            .ToList();

        return TypedResults.Ok(new MyOrdersResponse(products is not null, views));
    }

    private static MyOrderItemView ToItemView(
        OrderItemDto item, Dictionary<Guid, ProductSummaryDto>? products)
    {
        // Catalog is down: we know nothing about the product
        if (products is null)
            return new(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice,
                       null, null, null);

        // Catalog answered but the product is gone: not available anymore
        if (!products.TryGetValue(item.ProductId, out var p))
            return new(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice,
                       null, null, false);

        return new(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice,
                   string.IsNullOrEmpty(p.ImageUrl) ? null : p.ImageUrl,
                   p.Price, p.InStock);
    }
}