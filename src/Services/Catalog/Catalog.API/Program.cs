using Microsoft.EntityFrameworkCore;
using BuildingBlocks;
using Catalog.Infrastructure.Data;

LocalEnvLoader.LoadLocalDotEnv();   // ⭐ لو شغّلت المشروع على جهازك، حط سطر ConnectionStrings__DefaultConnection في ملف .env
var builder = WebApplication.CreateBuilder(args);


builder.AddServiceDefaults<CatalogDbContext>("Catalog");   // ⭐ بدل كل كود التسجيل القديم
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
