using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BuildingBlocks.Contracts;
namespace BuildingBlocks.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddRabbitMqEventBus(this IServiceCollection services)
    {
        services.AddOptions<RabbitMqOptions>()
            .BindConfiguration(RabbitMqOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();                          // ⭐ Fail Fast من الموديول 4

        services.AddSingleton<RabbitMqConnection>();     // اتصال واحد للخدمة كلها
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        return services;
    }

    // using BuildingBlocks.Contracts; و using Microsoft.Extensions.Logging;

        public static IServiceCollection AddEventHandler<TEvent, THandler>(
            this IServiceCollection services, string serviceName)
            where TEvent : IntegrationEvent
            where THandler : class, IIntegrationEventHandler<TEvent>
        {
            services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();

            var queueName = $"{serviceName}.{typeof(TEvent).Name}";   // "ordering.BasketCheckedOutIntegrationEvent"

            services.AddHostedService(sp => new RabbitMqConsumer<TEvent>(
                sp.GetRequiredService<RabbitMqConnection>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<RabbitMqConsumer<TEvent>>>(),
                queueName));

            return services;
        }
}