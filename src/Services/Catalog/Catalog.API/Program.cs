using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Catalog.Infrastructure.Data;
using Catalog.API.ErrorHandling;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



// register db context
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")!
     , sql =>
        sql.EnableRetryOnFailure() // دي عشان لو الداتا بيز وقعت فالنص يديك 503 بعد مايحاول كذا مره
    ));


//check if api connected to db or not
builder.Services.AddHealthChecks()
    // فحص "إنت صاحي؟" بيرجع Healthy على طول، لأن لو الكود ده اتنفذ يبقى البرنامج شغال
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    // فحص "إنت جاهز؟" بيجرب يكلم الداتابيز
    .AddDbContextCheck<CatalogDbContext>("catalog-db", tags: ["ready"]);

builder.Services.AddProblemDetails();                              // فعّل الشكل الموحد
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();    // سجّل صايد الأخطاء



builder.Services.AddSerilog(lc => lc
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Catalog")   // غيّرها لكل خدمة
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
        var context = services.GetRequiredService<CatalogDbContext>();
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
