using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Serilog;
using Basket.Domain.Contracts;
using Basket.Infrastructure.ExternalServices.Catalog;
using Basket.Infrastructure.Data;
using Basket.API.ErrorHandling;
LoadLocalDotEnv();

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


   // check if api connected to db or not -->
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<BasketDbContext>("basket-db", tags: ["ready"]);



builder.Services.AddProblemDetails();                              // فعّل الشكل الموحد
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();    // سجّل صايد الأخطاء


builder.Services.AddSerilog(lc => lc
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Basket")   // غيّرها لكل خدمة
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {TraceId} {Message:lj}{NewLine}{Exception}"));

var app = builder.Build();
app.UseExceptionHandler();   // ⭐ حطه في أول الـ pipeline عشان يمسك أي خطأ يحصل بعده
app.UseSerilogRequestLogging();   // سطر لوج واحد مرتب لكل طلب
// =====================================================================
// 🌟 كود عمل Database Update (Migration) أوتوماتيك أول ما المشروع يشتغل
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<BasketDbContext>();
        // السطر ده بيعمل نفس وظيفة dotnet ef database update بالظبط!
        await context.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "حصل خطأ أثناء عمل Migration للداتابيز وقت التشغيل.");
        throw; // ⭐ ارمي الخطأ تاني عشان الخدمة تقف
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//
// 
// app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

static void LoadLocalDotEnv()
{
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")))
        return;

    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    for (var depth = 0; depth < 5 && directory is not null; depth++, directory = directory.Parent)
    {
        var path = Path.Combine(directory.FullName, ".env");
        if (!File.Exists(path))
            continue;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }

        break;
    }

    var host = Environment.GetEnvironmentVariable("DB_HOST");
    var port = Environment.GetEnvironmentVariable("DB_PORT");
    var name = Environment.GetEnvironmentVariable("DB_NAME");
    var user = Environment.GetEnvironmentVariable("DB_USER");
    var password = Environment.GetEnvironmentVariable("DB_PASSWORD");
    if (host is null || port is null || name is null || user is null || password is null)
        return;

    Environment.SetEnvironmentVariable(
        "ConnectionStrings__DefaultConnection",
        $"Server={host},{port};Database={name};User Id={user};Password={password};TrustServerCertificate=True;Encrypt=True;");
}
