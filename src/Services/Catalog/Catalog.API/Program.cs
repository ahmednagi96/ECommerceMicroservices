using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Catalog.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



// register db context
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")!));


//check if api connected to db or not
builder.Services.AddHealthChecks()
    // فحص "إنت صاحي؟" بيرجع Healthy على طول، لأن لو الكود ده اتنفذ يبقى البرنامج شغال
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    // فحص "إنت جاهز؟" بيجرب يكلم الداتابيز
    .AddDbContextCheck<CatalogDbContext>("catalog-db", tags: ["ready"]);

var app = builder.Build();

// =====================================================================
// 🌟 كود عمل Database Update (Migration) أوتوماتيك أول ما المشروع يشتغل
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<CatalogDbContext>();
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
