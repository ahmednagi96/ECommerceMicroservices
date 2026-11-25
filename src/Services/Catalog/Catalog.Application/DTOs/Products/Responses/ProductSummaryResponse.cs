namespace Catalog.Application.DTOs.Products.Responses;

// Display data for other screens (e.g. "my orders")
public record ProductSummaryResponse(
    Guid Id,
    string Name,
    string ImageUrl,
    decimal Price,
    bool InStock);