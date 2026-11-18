using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required] public string HostName { get; init; } = default!;
    public int Port { get; init; } = 5672;
    [Required] public string UserName { get; init; } = default!;
    [Required] public string Password { get; init; } = default!;
}