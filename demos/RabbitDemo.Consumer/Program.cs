using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "ecommerce",
    Password = "1051be5cd97276aa1e0029a52b557c5b"
};

await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

const string exchange = "basket.events";
const string queue = "ordering.basket-checked-out";
const string dlx = "basket.events.dlx";                 // Exchange للرسائل الميتة
const string dlq = "ordering.basket-checked-out.dlq";   // طابور الرسائل الميتة

// 1) نجهز الـ DLQ الأول
await channel.ExchangeDeclareAsync(dlx, ExchangeType.Fanout, durable: true);
await channel.QueueDeclareAsync(dlq,durable: true, exclusive: false, autoDelete: false);
await channel.QueueBindAsync(dlq, dlx, routingKey: "");

// 2) الطابور الأساسي، ونقوله: "اللي يترفض يروح على dlx"
await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true);
var arg = new Dictionary<string, object?>
{
    ["x-dead-letter-exchange"] = "basket.events.dlx"
};
await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,arguments: arg
);
await channel.QueueBindAsync(queue, exchange, routingKey: string.Empty);

// 3) Prefetch: رسالة واحدة في إيدي لحد ما أخلصها
await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);

// 4) اللي هيحصل لما رسالة توصل
var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (sender, ea) =>
{
    var json = Encoding.UTF8.GetString(ea.Body.Span);
    Console.WriteLine($"📥 Received: {json}");
    try
    {
        if (json.Contains("\"fail\""))
            throw new InvalidOperationException("Simulated failure");

        await Task.Delay(3000); // بنمثّل إن عمل الطلب بياخد 3 ثواني

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);   // ✅ خلصت
        Console.WriteLine("✅ Done (Ack)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ {ex.Message} → DLQ");
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false); // ⚰️
    }
};

// 5) ابدأ الاستماع، و autoAck: false = أنا اللي هقول خلصت
await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer);

Console.WriteLine("Waiting for messages... press Enter to exit");
Console.ReadLine();