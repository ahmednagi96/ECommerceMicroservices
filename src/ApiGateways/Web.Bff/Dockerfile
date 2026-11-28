FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/ApiGateways/Web.Bff/Web.Bff.csproj", "src/ApiGateways/Web.Bff/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Authentication/BuildingBlocks.Authentication.csproj", "src/BuildingBlocks/BuildingBlocks.Authentication/"]
RUN dotnet restore "src/ApiGateways/Web.Bff/Web.Bff.csproj"

COPY src/ApiGateways/Web.Bff/ src/ApiGateways/Web.Bff/
COPY src/BuildingBlocks/BuildingBlocks.Authentication/ src/BuildingBlocks/BuildingBlocks.Authentication/
WORKDIR /app/src/ApiGateways/Web.Bff
RUN dotnet publish "Web.Bff.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Web.Bff.dll"]