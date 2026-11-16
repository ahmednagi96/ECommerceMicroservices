using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Domain.Orders;
using Ordering.Domain.ValueObjects;

namespace Ordering.Infrastructure.Data.Seed;

/// <summary>
/// بيانات تجريبية لخدمة الطلبات (للتطوير بس).
///
/// القواعد اللي ماشيين عليها:
/// 1) Idempotent: لو فيه أوردرات أصلًا، مش هنضيف تاني (تقدر تشغّل الأبلكيشن 100 مرة بأمان).
/// 2) بنبني الداتا عن طريق الـ Domain (Order.Create / AddItem / Confirm)
///    مش بنكتب في الجداول مباشرة، عشان كل القواعد (Validation) تتطبق على الداتا التجريبية برضه.
/// 3) بنمسح الـ Domain Events قبل الحفظ، عشان الـ Seed مايبعتش إيميلات أو رسائل وهمية.
/// </summary>
public static class OrderingDbSeeder
{
    private const string Currency = "EGP";

    // IDs ثابتة للمنتجات، عشان نفس المنتج يبقى ليه نفس الـ Id في كل مرة
    // (ولو عملت Seed للـ Catalog بعدين، استخدم نفس الـ Guids دي عشان الداتا تبقى متناسقة).
    private static readonly Guid Laptop     = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mouse      = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Keyboard   = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Headphones = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Monitor    = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static async Task SeedAsync(
        OrderingDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        // ① Idempotency check
        if (await db.Orders.AnyAsync(ct))
        {
            logger.LogInformation("Ordering seed skipped: orders already exist.");
            return;
        }

        // ② نبني الأوردرات عن طريق الـ Domain
        var orders = BuildOrders();

        // ③ الداتا التجريبية مش أحداث حقيقية، فمش عايزين الـ handlers تشتغل
        foreach (var order in orders)
            order.ClearDomainEvents();

        // ④ نضيف ونحفظ مرة واحدة (Transaction واحدة)
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Ordering seed completed: {Count} orders added.", orders.Count);
    }

    private static List<Order> BuildOrders()
    {
        // ⚠️ لازم Address جديد لكل أوردر (عشان كده Func مش متغير):
        // Address متعرّف كـ Owned Entity في EF، و EF بيتتبعه بالـ Reference.
        // لو نفس الـ instance اتشارك بين أوردرين، EF بيربطه بواحد بس
        // والتاني بيتحفظ بأعمدة Shipping_* = NULL ← SqlException 515.
        Address cairo() => Address.Create("15 Tahrir St", "Cairo", "Egypt", "11511");
        Address alex()  => Address.Create("22 Corniche Rd", "Alexandria", "Egypt", "21500");
        Address giza()  => Address.Create("8 Pyramids St", "Giza", "Egypt", "12511");

        // أوردر 1: Pending — لسه العميل بيضيف فيه
        var order1 = Order.Create("user-1", cairo(), Currency);
        order1.AddItem(Laptop, "Dell XPS 13", Price(45_000m), 1);
        order1.AddItem(Mouse, "Logitech MX Master 3", Price(3_500m), 2);

        // أوردر 2: Confirmed
        var order2 = Order.Create("user-1", cairo(), Currency);
        order2.AddItem(Keyboard, "Keychron K2", Price(4_200m), 1);
        order2.AddItem(Headphones, "Sony WH-1000XM5", Price(16_000m), 1);
        order2.Confirm();

        // أوردر 3: Confirmed لعميل تاني
        var order3 = Order.Create("user-2", alex(), Currency);
        order3.AddItem(Monitor, "LG UltraGear 27\"", Price(12_500m), 2);
        order3.Confirm();

        // أوردر 4: Cancelled
        var order4 = Order.Create("user-2", alex(), Currency);
        order4.AddItem(Mouse, "Logitech MX Master 3", Price(3_500m), 1);
        order4.Cancel();

        // أوردر 5: Pending وفيه نفس المنتج اتضاف مرتين ← AddItem هتجمع الكمية (3)
        var order5 = Order.Create("user-3", giza(), Currency);
        order5.AddItem(Headphones, "Sony WH-1000XM5", Price(16_000m), 1);
        order5.AddItem(Headphones, "Sony WH-1000XM5", Price(16_000m), 2);
        order5.AddItem(Keyboard, "Keychron K2", Price(4_200m), 1);

        return [order1, order2, order3, order4, order5];
    }

    private static Money Price(decimal amount) => Money.Of(amount, Currency);
}
