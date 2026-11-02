namespace Basket.Application.DTOs.Items.Requests;

public record AddItemRequestDTO(string UserName, int ProductId, int Quantity);