namespace Catalog.Application.DTOs.Products.Responses;


public sealed record ProductDetailsResponse(
    Guid Id, string Name, string Description, decimal Price, long PriceVersion, string ImageUrl);