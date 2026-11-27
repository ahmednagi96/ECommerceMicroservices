using BuildingBlocks.Authentication;
using Web.Bff.Clients;
using Web.Bff.MyOrders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Registers JWT bearer authentication + authorization services (required by UseAuthentication/UseAuthorization)
builder.AddJwtAuthentication();

// Needed by ForwardAccessTokenHandler to read the incoming request's Authorization header
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ForwardAccessTokenHandler>();

// Ordering: the core of the page, can wait a bit
builder.Services.AddHttpClient<OrderingClient>(c =>
     c.BaseAddress = new Uri(builder.Configuration["Services:Ordering"]!))
    .AddHttpMessageHandler<ForwardAccessTokenHandler>()   // NEW
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
    });

// Catalog: only an enhancement, give up fast
builder.Services.AddHttpClient<CatalogClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]!))
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);
    });


var app = builder.Build();



app.UseExceptionHandler();
app.MapHealthChecks("/health/live");
app.UseAuthentication();
app.UseAuthorization();    // NEW
app.MapMyOrders();
app.Run();
