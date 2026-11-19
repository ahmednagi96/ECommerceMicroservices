using System.Text.Json;

namespace Basket.Infrastructure.Outbox;

public class OutboxMessage
{
    public Guid Id { get; set; }               // Same Id as the IntegrationEvent
    public string Type { get; set; } = "";     // Which C# type to rebuild later
    public string Content { get; set; } = "";  // The event itself as JSON
    public DateTime OccurredOn { get; set; }
    public DateTime? ProcessedOn { get; set; } // null = not sent yet
    public string? Error { get; set; }         // Last error if publishing failed

    public static OutboxMessage Create(Guid id, DateTime occurredOn, object message) => new()
    {
        Id = id,
        Type = message.GetType().AssemblyQualifiedName!,
        Content = JsonSerializer.Serialize(message, message.GetType()),
        OccurredOn = occurredOn
    };
}