namespace Basket.Application.DTOs.Checkout.Responses;

public sealed record PriceChangeDTO(
    Guid ProductId, string ProductName, decimal OldPrice, decimal NewPrice);

public sealed record CheckoutConflictResponseDTO(
    string Message,
    IReadOnlyList<PriceChangeDTO> PriceChanges,
    IReadOnlyList<string> RemovedProducts);