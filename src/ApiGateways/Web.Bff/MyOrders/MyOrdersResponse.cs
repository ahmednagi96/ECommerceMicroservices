namespace Web.Bff.MyOrders;

public sealed record MyOrdersResponse(
    bool ProductDetailsAvailable,
    IReadOnlyList<MyOrderView> Orders);

public sealed record MyOrderView(
    Guid OrderId, string Status, DateTime CreatedAt,
    string Currency, decimal Total, IReadOnlyList<MyOrderItemView> Items);

public sealed record MyOrderItemView(
    Guid ProductId,
    string Name,            // from Ordering (name at purchase time)
    int Quantity,
    decimal PaidPrice,      // from Ordering
    string? ImageUrl,       // from Catalog (null if unknown)
    decimal? CurrentPrice,  // from Catalog
    bool? IsAvailable);     // from Catalog