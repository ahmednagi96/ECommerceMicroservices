using Catalog.Domain.Exceptions;
namespace Catalog.Domain.Catalog;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; }=string.Empty;
    public string Description { get; set; }=string.Empty;
    public decimal Price { get; private set; }
    public long PriceVersion { get; private set; }

    public int Stock { get; set; }
    public string ImageUrl { get; set; }=string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }


    public bool ChangePrice(decimal newPrice)
    {
        if (newPrice <= 0)
            throw new DomainException("Price must be greater than zero.");

        if (newPrice == Price)
            return false; // same price: nothing changed, no event

        Price = newPrice;
        PriceVersion++;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }
}