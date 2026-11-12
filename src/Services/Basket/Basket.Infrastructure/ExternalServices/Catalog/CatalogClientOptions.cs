using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.ExternalServices.Catalog;
public sealed class CatalogClientOptions
{
    public const string SectionName = "CatalogClient";

    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;
}