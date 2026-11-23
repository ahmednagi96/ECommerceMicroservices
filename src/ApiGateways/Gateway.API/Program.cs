var builder = WebApplication.CreateBuilder(args);

// Register YARP and read routes/clusters from the "ReverseProxy" config section
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();   // NEW

var app = builder.Build();
app.MapHealthChecks("/health/live");  // NEW: "is the gateway process alive?"

// Add the proxy to the request pipeline
app.MapReverseProxy();

app.Run();