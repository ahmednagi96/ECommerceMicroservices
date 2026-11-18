using System.Text.Json;
using BuildingBlocks.Contracts;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqEventBus(RabbitMqConnection connection, ILogger<RabbitMqEventBus> logger)
    : IEventBus
{
    public const string ExchangeName = "ecommerce.events";

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        // 1) العنوان = اسم الرسالة
        var routingKey = typeof(TEvent).Name;   // "BasketCheckedOutIntegrationEvent"

        // 2) الرسالة ← bytes
        var body = JsonSerializer.SerializeToUtf8Bytes(@event);

        // 3) الاتصال (نفس الاتصال دايمًا) + خط شغل جديد
        var conn = await connection.GetAsync(ct);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);

        // 4) اتأكد إن أوضة الفرز موجودة
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct, durable: true, cancellationToken: ct);

        // 5) بيانات على الظرف
        var props = new BasicProperties
        {
            Persistent = true,                  // تتكتب على الديسك
            ContentType = "application/json",
            MessageId = @event.Id.ToString(),   // رقم الرسالة
            Type = routingKey                   // نوعها
        };

        // 6) ابعت 📤
        await channel.BasicPublishAsync(ExchangeName, routingKey, mandatory: false,
            basicProperties: props, body: body, cancellationToken: ct);

        logger.LogInformation("📤 Published {EventName} {EventId}", routingKey, @event.Id);
    }
}