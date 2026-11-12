using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Serilog;
using Microsoft.Extensions.Options;
using Basket.Domain.Contracts;
using Basket.Infrastructure.ExternalServices.Catalog;
using Basket.Infrastructure.Data;
using BuildingBlocks;
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
builder.Services.AddDbContext<BasketDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")!
    , sql =>
        sql.EnableRetryOnFailure()
    ));


builder.AddServiceDefaults<BasketDbContext>("Basket");   // ⭐ سجل كل حاجة من اللوجز وفحص الصحة وشكل الأخطاء


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

