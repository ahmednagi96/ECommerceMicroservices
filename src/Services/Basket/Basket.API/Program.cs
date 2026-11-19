using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Basket.Domain.Contracts;
using Basket.Infrastructure.ExternalServices.Catalog;
using Basket.Infrastructure.Data;
using BuildingBlocks;
using Wolverine;
using Wolverine.RabbitMQ;
using BuildingBlocks.Contracts.Basket;
using Wolverine.SqlServer;
using Wolverine.EntityFrameworkCore;
using JasperFx.Resources;

LocalEnvLoader.LoadLocalDotEnv();   // ⭐ لو شغّلت المشروع على جهازك، حط سطر CatalogClient__BaseUrl في ملف .env
var builder = WebApplication.CreateBuilder(args);


builder.Services.AddOptions<CatalogClientOptions>()
    .BindConfiguration(CatalogClientOptions.SectionName) // اربط الكلاس بقسم "CatalogClient"
    .ValidateDataAnnotations()                           // افحص الـ [Required] و [Url]
    .ValidateOnStart();                                  // ⭐ افحص وقت التشغيل، مش وقت أول طلب

// عنوان المعرض يأتي من CatalogClient__BaseUrl (في ملف .env أو docker-compose)
builder.Services.AddHttpClient<ICatalogService, CatalogService>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<CatalogClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
})
.AddStandardResilienceHandler();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// register db context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<BasketDbContext>(
    options => options.UseSqlServer(connectionString),   // EnableRetryOnFailure removed
    optionsLifetime: ServiceLifetime.Singleton);

builder.AddServiceDefaults<BasketDbContext>("Basket");   // ⭐ سجل كل حاجة من اللوجز وفحص الصحة وشكل الأخطاء
//builder.Services.AddRabbitMqEventBus();   // ⭐ سجل كل حاجة من اللوجز وفحص الصحة وشكل الأخطاء
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
    opts.PublishMessage<BasketCheckedOutIntegrationEvent>()
        .ToRabbitExchange("basket-checked-out");
});
// NEW 4) Create Wolverine's tables at startup
builder.Services.AddResourceSetupOnStartup();
var app = builder.Build();


app.UseServiceDefaults();   // ⭐ استخدم كل حاجة من اللوجز وفحص الصحة وشكل الأخطاء
await app.MigrateDatabaseAsync<BasketDbContext>();   // ⭐ بدل كود الـ migration
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

