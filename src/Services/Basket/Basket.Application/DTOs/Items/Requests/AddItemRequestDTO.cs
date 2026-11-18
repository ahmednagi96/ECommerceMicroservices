namespace Basket.Application.DTOs.Items.Requests;

public record AddItemRequestDTO(string UserName, Guid ProductId, int Quantity);