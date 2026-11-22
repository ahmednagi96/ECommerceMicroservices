
using Microsoft.EntityFrameworkCore;
using BuildingBlocks;
using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;
using Wolverine.EntityFrameworkCore;
using Catalog.Infrastructure.Data;
using BuildingBlocks.Contracts.Catalog;
using JasperFx.Resources;
using Catalog.Application.Queries;
using Catalog.Infrastructure.Queries;
using Microsoft.Extensions.Caching.Hybrid;

LocalEnvLoader.LoadLocalDotEnv();   // ⭐ لو شغّلت المشروع على جهازك، حط سطر ConnectionStrings__DefaultConnection في ملف .env
var builder = WebApplication.CreateBuilder(args);


builder.AddServiceDefaults<CatalogDbContext>("Catalog");   // ⭐ بدل كل كود التسجيل القديم
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<CatalogDbContext>(
    options => options.UseSqlServer(connectionString),   // EnableRetryOnFailure removed
    optionsLifetime: ServiceLifetime.Singleton);

// Read-side queries (DbContext + HybridCache)
builder.Services.AddScoped<ICatalogQueries, CatalogQueries>();

builder.Host.UseWolverine(opts =>
{
    opts.UseRuntimeCompilation();   // 👈 الجديد
    // 1) الاتصال (نفس متغيرات الـ .env)
    opts.UseRabbitMq(f =>
    {
        f.HostName = builder.Configuration["RabbitMq:HostName"]!;
        f.UserName = builder.Configuration["RabbitMq:UserName"]!;
        f.Password = builder.Configuration["RabbitMq:Password"]!;
    })
    .AutoProvision();   // اعمل الـ Exchanges والـ Queues لوحدك


  // NEW 1) Store outgoing messages in Basket's own database (schema "wolverine")
    opts.PersistMessagesWithSqlServer(connectionString, "wolverine");

  // NEW 2) Let Wolverine join EF Core's transaction
    opts.UseEntityFrameworkCoreTransactions();

    // NEW 3) Every outgoing message goes through the durable outbox
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();


    // 4) الرسالة دي تروح على الـ Exchange ده
    opts.PublishMessage<ProductPriceChangedIntegrationEvent>()
        .ToRabbitExchange("product-price-changed");
});
// NEW 4) Create Wolverine's tables at startup
builder.Services.AddResourceSetupOnStartup();    


// L2: Redis (shared by all Catalog instances)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "catalog:";   // every key starts with "catalog:"
});

// HybridCache = L1 (memory) + L2 (Redis above, picked up automatically)
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),           // how long in Redis
        LocalCacheExpiration = TimeSpan.FromSeconds(30)  // how long in memory (short on purpose)
    };
});

var app = builder.Build();

app.UseServiceDefaults();                       // ⭐ بدل كود الـ middleware والـ health
await app.MigrateDatabaseAsync<CatalogDbContext>();   // ⭐ بدل كود الـ migration


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
