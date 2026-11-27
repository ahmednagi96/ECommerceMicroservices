using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Authentication;

public static class JwtAuthenticationExtensions
{
    public static WebApplicationBuilder AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"]!;
                options.Audience = builder.Configuration["Auth:Audience"];
                // Secure by default; only local environments turn it off
                options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", defaultValue: true);                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
            });

        builder.Services.AddAuthorization();
        return builder;
    }
}