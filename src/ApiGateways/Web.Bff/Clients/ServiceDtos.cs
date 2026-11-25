namespace Web.Bff.Clients;

// Our own copies of the services' JSON.
// We do NOT reference Ordering/Catalog projects.
public sealed record OrderDto(
    Guid Id, string Status, DateTime CreatedAt,
    string Currency, decimal Total, IReadOnlyList<OrderItemDto> Items);

public sealed record OrderItemDto(
    Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record ProductSummaryDto(
    Guid Id, string Name, string ImageUrl, decimal Price, bool InStock);