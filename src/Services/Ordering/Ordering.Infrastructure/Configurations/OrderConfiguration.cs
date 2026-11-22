using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain.Orders;
using Ordering.Domain.ValueObjects;

namespace Ordering.Infrastructure.Configurations;   

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        // 1) OrderId ← Guid في الداتابيز
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OrderId(value));

        // 7) One order per checkout (idempotency safety net)
        builder.HasIndex(o => o.CheckoutId).IsUnique();    

        builder.Property(o => o.BuyerId).HasMaxLength(100).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();

        // 2) Enum ← نص في الداتابيز (أوضح من أرقام)
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

        // 3) Address ← أعمدة جوه جدول Orders
        builder.OwnsOne(o => o.ShippingAddress, a =>
        {
            a.Property(x => x.Street).HasColumnName("Shipping_Street").HasMaxLength(200);
            a.Property(x => x.City).HasColumnName("Shipping_City").HasMaxLength(100);
            a.Property(x => x.Country).HasColumnName("Shipping_Country").HasMaxLength(100);
            a.Property(x => x.ZipCode).HasColumnName("Shipping_ZipCode").HasMaxLength(20);
        });

        // 4) Items ← جدول OrderItems تابع للـ Order
        builder.OwnsMany(o => o.Items, items =>
        {
            items.ToTable("OrderItems");
            items.WithOwner().HasForeignKey("OrderId");
            items.Property<int>("Id");
            items.HasKey("Id");

            items.Property(i => i.ProductName).HasMaxLength(200);

            // Money جوه OrderItem ← عمودين
            items.OwnsOne(i => i.UnitPrice, m =>
            {
                m.Property(x => x.Amount).HasColumnName("UnitPrice").HasPrecision(18, 2);
                m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3);
            });
        });

        // 5) خلي EF يكتب في _items مش في Items
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // 6) الأحداث مش أعمدة
        builder.Ignore(o => o.DomainEvents);
    }
}