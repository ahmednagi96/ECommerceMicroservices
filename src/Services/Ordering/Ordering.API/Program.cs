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
LocalEnvLoader.LoadLocalDotEnv();   // ⭐ لو شغّلت المشروع على جهاز


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults<OrderingDbContext>("Ordering");   // ⭐ بدل كل كود التسجيل القديم

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



// register db context
builder.Services.AddDbContext<OrderingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")!
     , sql =>
        sql.EnableRetryOnFailure()
    ));

builder.Services.AddScoped<DomainEventDispatcher>();
builder.Services.AddScoped<IDomainEventHandler<OrderConfirmedDomainEvent>, OrderConfirmedHandler>();
builder.Services.AddScoped<IDomainEventHandler<OrderCancelledDomainEvent>, OrderCancelledHandler>();    
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

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
