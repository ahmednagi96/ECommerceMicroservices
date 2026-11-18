using System.Text.Json;
using BuildingBlocks.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqConsumer<TEvent>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqConsumer<TEvent>> logger,
    string queueName) : BackgroundService
    where TEvent : IntegrationEvent
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var exchange = RabbitMqEventBus.ExchangeName;   // "ecommerce.events"
        var routingKey = typeof(TEvent).Name;            // "BasketCheckedOutIntegrationEvent"
        var dlx = $"{exchange}.dlx";
        var dlq = $"{queueName}.dlq";

        var conn = await connection.GetAsync(stoppingToken);
        _channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);

        // ① صندوق المرتجعات (نفس 6.3)
        await _channel.ExchangeDeclareAsync(dlx, ExchangeType.Direct, durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(dlq, dlx, routingKey: queueName, cancellationToken: stoppingToken);

        // ② الطابور الأساسي + ورقة "المرفوض يروح فين"
        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true, cancellationToken: stoppingToken);
        var args = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = dlx,
            ["x-dead-letter-routing-key"] = queueName
        };
        await _channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false,
            arguments: args, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(queueName, exchange, routingKey, cancellationToken: stoppingToken);

        // ③ واحدة واحدة 🍽️
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        // ④ لما رسالة توصل
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var @event = JsonSerializer.Deserialize<TEvent>(ea.Body.Span)
                    ?? throw new InvalidOperationException("Empty message.");

                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
                await handler.HandleAsync(@event, stoppingToken);

                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);            // ✅
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Failed to handle {EventName}. Sent to DLQ.", routingKey);
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false); // ⚰️
            }
        };

        // ⑤ ابدأ الاستماع
        await _channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        logger.LogInformation("👂 Listening on {Queue}", queueName);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.CloseAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}