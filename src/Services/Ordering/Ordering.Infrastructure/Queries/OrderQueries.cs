using Microsoft.EntityFrameworkCore;
using Ordering.Application.DTOs.Orders.Queries;
using Ordering.Application.Orders.Queries;
using Ordering.Infrastructure.Data;

namespace Ordering.Infrastructure.Queries;

public sealed class OrderQueries(OrderingDbContext db) : IOrderQueries
{
    public async Task<IReadOnlyList<OrderDetailsDTO>> GetByBuyerAsync(string buyerId, CancellationToken ct) =>
        await db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDetailsDTO(
                o.Id.Value,
                o.Status.ToString(),
                o.CreatedAt,
                o.Currency,
                o.Items.Sum(i => i.UnitPrice.Amount * i.Quantity),   // computed in SQL
                o.Items.Select(i => new OrderItemDto(
                    i.ProductId, i.ProductName, i.UnitPrice.Amount, i.Quantity)).ToList()))
            .ToListAsync(ct);
}