namespace Catalog.Application.DTOs.Products.Responses;

public record ProductResponse(Guid Id, string Name, decimal Price, long PriceVersion);
