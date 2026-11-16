namespace Ordering.Domain.Orders;
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct);
    void Add(Order order);
    Task SaveChangesAsync(CancellationToken ct);
}