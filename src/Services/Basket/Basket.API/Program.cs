using Basket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Basket.Domain.Contracts;
using Basket.Infrastructure.Services;
using Microsoft.Extensions.Http.Resilience;

LoadLocalDotEnv();

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")!));



var app = builder.Build();

// =====================================================================
// 🌟 كود عمل Database Update (Migration) أوتوماتيك أول ما المشروع يشتغل
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        // السطر ده بيعمل نفس وظيفة dotnet ef database update بالظبط!
        await context.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "حصل خطأ أثناء عمل Migration للداتابيز وقت التشغيل.");
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
