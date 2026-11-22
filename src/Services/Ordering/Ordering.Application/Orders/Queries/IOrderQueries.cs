using Ordering.Application.DTOs.Orders.Queries;

namespace Ordering.Application.Orders.Queries;

public interface IOrderQueries
{
    Task<IReadOnlyList<OrderDetailsDTO>> GetByBuyerAsync(string buyerId, CancellationToken ct);
}
