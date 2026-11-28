namespace Basket.Application.DTOs.Items.Requests;

public record AddItemRequestDTO(Guid ProductId, int Quantity);