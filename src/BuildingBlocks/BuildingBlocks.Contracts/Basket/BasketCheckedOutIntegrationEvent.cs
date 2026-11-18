namespace BuildingBlocks.Contracts.Basket;

public sealed record BasketCheckedOutIntegrationEvent(
    string BuyerId,
    string Currency,
    CheckoutAddress ShippingAddress,
    IReadOnlyList<CheckoutItem> Items) : IntegrationEvent;

public sealed record CheckoutItem(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record CheckoutAddress(
    string Street,
    string City,
    string Country,
    string ZipCode);