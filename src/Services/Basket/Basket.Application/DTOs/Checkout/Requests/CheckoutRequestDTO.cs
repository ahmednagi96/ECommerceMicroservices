using System.ComponentModel.DataAnnotations;

namespace Basket.Application.DTOs.Checkout.Requests;

public record CheckoutRequestDTO(
    [Required] string Street,
    [Required] string City,
    [Required] string Country,
    [Required] string ZipCode);
