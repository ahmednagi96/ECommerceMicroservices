using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Serilog;
using Basket.Domain.Contracts;
using Basket.Infrastructure.ExternalServices.Catalog;
using Basket.Infrastructure.Data;
using BuildingBlocks;
LocalEnvLoader.LoadLocalDotEnv();   // ⭐ لو شغّلت المشروع على جهازك، حط سطر CATALOG_URL في ملف .env
var builder = WebApplication.CreateBuilder(args);


builder.Services.AddOptions<CatalogClientOptions>()
    .BindConfiguration(CatalogClientOptions.SectionName) // اربط الكلاس بقسم "CatalogClient"
    .ValidateDataAnnotations()                           // افحص الـ [Required] و [Url]
    .ValidateOnStart();                                  // ⭐ افحص وقت التشغيل، مش وقت أول طلب

// عنوان المعرض يأتي من سطر CATALOG_URL في ملف .env
builder.Services.AddHttpClient<ICatalogService, CatalogService>(client =>
{
    var catalogUrl = Environment.GetEnvironmentVariable("CATALOG_URL");
    if (string.IsNullOrWhiteSpace(catalogUrl))
        throw new InvalidOperationException("عنوان خدمة المعرض غير موجود. ضعه في سطر CATALOG_URL داخل ملف .env.");

    client.BaseAddress = new Uri(catalogUrl);
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

