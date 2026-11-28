# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Central settings (framework + package versions) before restore
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]

# Only the gateway project (it references no other project)
COPY ["src/ApiGateways/Gateway.API/Gateway.API.csproj", "src/ApiGateways/Gateway.API/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Authentication/BuildingBlocks.Authentication.csproj", "src/BuildingBlocks/BuildingBlocks.Authentication/"]

RUN dotnet restore "src/ApiGateways/Gateway.API/Gateway.API.csproj"

COPY src/ApiGateways/Gateway.API/ src/ApiGateways/Gateway.API/
COPY src/BuildingBlocks/BuildingBlocks.Authentication/ src/BuildingBlocks/BuildingBlocks.Authentication/
WORKDIR /app/src/ApiGateways/Gateway.API
RUN dotnet publish "Gateway.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# curl for the compose healthcheck
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Gateway.API.dll"]