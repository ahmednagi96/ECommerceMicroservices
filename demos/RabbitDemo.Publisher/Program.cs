
Console.WriteLine("Hello, World!");
/* using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

// 1) الاتصال: نفس اليوزر اللي في rabbitmq.env
var factory = new ConnectionFactory
{
    HostName = "localhost",   // بنشغّل الـ Demo من جهازك، مش من جوه Docker
    UserName = "ecommerce",
    Password = "1051be5cd97276aa1e0029a52b557c5b"
};

await using var connection = await factory.CreateConnectionAsync(); // سلك واحد للسيرفر
await using var channel = await connection.CreateChannelAsync();    // خط شغل جوه السلك

// 2) نتأكد إن الـ Exchange موجود (لو موجود مش هيعمل حاجة)
const string exchange = "basket.events";
await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true);

Console.WriteLine("Type a buyerId and press Enter (type 'fail' to test DLQ, 'exit' to quit):");

while (Console.ReadLine() is { } input && input != "exit")
{
    // 3) الرسالة: Object ← JSON ← bytes
    var message = new { BasketId = Guid.NewGuid(), BuyerId = input, Total = 500m };
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

    // 4) Persistent = تتكتب على الديسك
    var props = new BasicProperties { Persistent = true, ContentType = "application/json" };

    // 5) ننشر على الـ Exchange (مش على الطابور!)
    await channel.BasicPublishAsync(exchange, routingKey: string.Empty,
        mandatory: false, basicProperties: props, body: body);

    Console.WriteLine($"📤 Sent: {input}");
} */