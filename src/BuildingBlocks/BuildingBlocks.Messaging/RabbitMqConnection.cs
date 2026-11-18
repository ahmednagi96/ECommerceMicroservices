using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is not null) return _connection;   // موجود؟ رجّعه على طول

        await _lock.WaitAsync(ct);                          // 🚪 واحد بس يدخل
        try
        {
            if (_connection is not null) return _connection;

            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.HostName,
                Port = o.Port,
                UserName = o.UserName,
                Password = o.Password
            };

            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _lock.Release();                                // 🚪 افتح الباب للي بعده
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}