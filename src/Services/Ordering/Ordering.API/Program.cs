using Microsoft.EntityFrameworkCore;
using Ordering.Infrastructure.Data;
using BuildingBlocks;
using Ordering.Application.Common;
using Ordering.Application.Orders.EventHandlers;
using Ordering.Domain.Orders.Events;
using Ordering.Infrastructure.Events;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Repositories;
using Ordering.Infrastructure.Data.Seed;
//using BuildingBlocks.Messaging;
//using BuildingBlocks.Contracts.Basket;
//using Ordering.API.IntegrationEvents;
using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.ErrorHandling;
using Ordering.Domain.Exceptions;
using JasperFx.CodeGeneration.Model;   // 👈 فيه ServiceLocationPolicy
using Wolverine.SqlServer;
using JasperFx.Resources;
using Ordering.Application.Orders.Queries;
using Ordering.Infrastructure.Queries;
LocalEnvLoader.LoadLocalDotEnv();   // ⭐ لو شغّلت المشروع على جهاز


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults<OrderingDbContext>("Ordering");   // ⭐ بدل كل كود التسجيل القديم

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

// register db context
builder.Services.AddDbContext<OrderingDbContext>(
options => options.UseSqlServer(connectionString, sql =>
    sql.EnableRetryOnFailure(
        maxRetryCount: 2,
        maxRetryDelay: TimeSpan.FromSeconds(2),
        errorNumbersToAdd: null)),    optionsLifetime: ServiceLifetime.Singleton);

builder.Services.AddScoped<DomainEventDispatcher>();
builder.Services.AddScoped<IDomainEventHandler<OrderConfirmedDomainEvent>, OrderConfirmedHandler>();
builder.Services.AddScoped<IDomainEventHandler<OrderCancelledDomainEvent>, OrderCancelledHandler>();    
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderQueries, OrderQueries>();

//builder.Services.AddRabbitMqEventBus();
//builder.Services.AddEventHandler<BasketCheckedOutIntegrationEvent, BasketCheckedOutHandler>("ordering");

builder.Host.UseWolverine(opts =>
{
    opts.UseRuntimeCompilation();   // 👈 الجديد
    opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;   // 👈 "اسأل عادي، بس اكتب تحذير"

    // 1) الاتصال + اربط طابورنا بالـ Exchange بتاع الرسالة
    opts.UseRabbitMq(f =>
    {
        f.HostName = builder.Configuration["RabbitMq:HostName"]!;
        f.UserName = builder.Configuration["RabbitMq:UserName"]!;
        f.Password = builder.Configuration["RabbitMq:Password"]!;
    })
    .DeclareExchange("basket-checked-out", ex => ex.BindQueue("ordering.basket-checked-out"))
    .AutoProvision();
    // NEW 1) Store incoming messages in Ordering's own database (schema "wolverine")
    opts.PersistMessagesWithSqlServer(connectionString, "wolverine");

    // NEW 2) Listen through the durable inbox
    opts.ListenToRabbitQueue("ordering.basket-checked-out")
        .UseDurableInbox();

    // 3) لو الخطأ "قاعدة بيزنس اتكسرت" ← الـ DLQ على طول، مفيش فايدة من المحاولة
    opts.OnException<DomainException>().MoveToErrorQueue();

    // 4) Anything else: short retries, then longer scheduled retries, then DLQ
    opts.OnException<Exception>()
    .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
    .Then.ScheduleRetry(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5))
    .Then.MoveToErrorQueue();
});
// NEW 3) Create Wolverine's tables at startup
builder.Services.AddResourceSetupOnStartup();
var app = builder.Build();

app.UseServiceDefaults();                       // ⭐ بدل كود الـ middleware والـ health
await app.MigrateDatabaseAsync<OrderingDbContext>();   // ⭐ بدل كود الـ migration

// 🌱 Seed: بعد الـ Migration (لازم الجداول تكون موجودة)، وفي الـ Development بس
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    await OrderingDbSeeder.SeedAsync(db, app.Logger);
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.Run();
