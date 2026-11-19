using System.Text.Json;
using Basket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Basket.API.Outbox;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5); //plling
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Never let the loop die (e.g. DB is down for a moment)
                logger.LogError(ex, "Outbox processing failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        // A BackgroundService is a Singleton, but DbContext is Scoped
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BasketDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // a) Get messages that were not sent yet (oldest first)
        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedOn == null)
            .OrderBy(m => m.OccurredOn)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            try
            {
                // b) Rebuild the real object from Type + JSON, then publish
                var type = Type.GetType(message.Type, throwOnError: true)!;
                var @event = JsonSerializer.Deserialize(message.Content, type)!;
                await bus.PublishAsync(@event);

                // c) Mark it as sent
                message.ProcessedOn = DateTime.UtcNow;
                message.Error = null;
            }
            catch (Exception ex)
            {
                // Keep it in the table; we will try again next round
                message.Error = ex.Message;
                logger.LogWarning(ex, "Failed to publish outbox message {MessageId}", message.Id);
            }

            await db.SaveChangesAsync(ct); // Save after EACH message
        }
    }
}