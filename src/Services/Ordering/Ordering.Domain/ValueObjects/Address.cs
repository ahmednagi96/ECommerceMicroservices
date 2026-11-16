
using Ordering.Domain.Exceptions;
namespace Ordering.Domain.ValueObjects;

public sealed record Address
{
    public string Street { get; }
    public string City { get; }
    public string Country { get; }
    public string ZipCode { get; }

    private Address(string street, string city, string country, string zipCode)
    {
        Street = street;
        City = city;
        Country = country;
        ZipCode = zipCode;
    }

    public static Address Create(string street, string city, string country, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(street)) throw new DomainException("Street is required.");
        if (string.IsNullOrWhiteSpace(city)) throw new DomainException("City is required.");
        if (string.IsNullOrWhiteSpace(country)) throw new DomainException("Country is required.");

        return new Address(street.Trim(), city.Trim(), country.Trim(), zipCode?.Trim() ?? "");
    }
}