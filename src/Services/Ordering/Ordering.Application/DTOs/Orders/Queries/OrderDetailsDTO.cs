namespace Ordering.Application.DTOs.Orders.Queries;

public sealed record OrderItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderDetailsDTO(
    Guid Id,
    string Status,
    DateTime CreatedAt,
    string Currency,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items);