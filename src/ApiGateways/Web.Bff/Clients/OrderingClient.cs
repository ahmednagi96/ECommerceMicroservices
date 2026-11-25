namespace Web.Bff.Clients;

public sealed class OrderingClient(HttpClient http)
{
    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string buyerId, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<OrderDto>>(
            $"api/orders?buyerId={Uri.EscapeDataString(buyerId)}", ct) ?? [];
}