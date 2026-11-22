namespace BuildingBlocks.Contracts.Catalog;

public sealed record ProductPriceChangedIntegrationEvent(
    Guid ProductId,
    decimal NewPrice,
    long Version) : IntegrationEvent;