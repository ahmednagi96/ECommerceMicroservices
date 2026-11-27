namespace Web.Bff.Clients;

public sealed class OrderingClient(HttpClient http)
{
    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<OrderDto>>(
            $"api/orders", ct) ?? [];
}