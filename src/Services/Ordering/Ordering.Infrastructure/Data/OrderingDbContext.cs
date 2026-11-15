
using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Ordering.Domain.Common;
using Ordering.Infrastructure.Events;
namespace Ordering.Infrastructure.Data;
public class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    DomainEventDispatcher dispatcher) : DbContext(options)
{
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // 1) نجمع الأحداث من كل الـ Aggregates
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count != 0)
            .ToList();

        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        aggregates.ForEach(a => a.ClearDomainEvents());

        // 2) نحفظ الأول
        var result = await base.SaveChangesAsync(ct);

        // 3) نوزع بعد ما الحفظ نجح
        await dispatcher.DispatchAsync(events, ct);

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Ignore(o => o.DomainEvents); // مش عمود في الداتابيز
        // ...
    }
}