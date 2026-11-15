using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Data;
namespace Ordering.Infrastructure.Repositories;
public class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    public void Add(Order order) => db.Orders.Add(order);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}