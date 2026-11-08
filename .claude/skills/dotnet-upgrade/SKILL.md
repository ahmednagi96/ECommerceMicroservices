---
name: dotnet-upgrade
description: Use when upgrading an ASP.NET Core solution between .NET major versions (e.g. 8 -> 10), centralizing TargetFramework and NuGet versions (Directory.Build.props + Central Package Management), or fixing NuGet vulnerability warnings (NU1902/NU1903/NU1904).
---

# Upgrade ASP.NET Core (.NET 8 -> .NET 10) + Centralize Versions

Goal: one place for the framework version, one place for package versions,
and an upgrade that is two lines + Dockerfile + CI.

Always do the work in **two separate PRs** so a failure points to one cause:
1. **PR 1 – Centralize** (stay on the current .NET version).
2. **PR 2 – Upgrade** (.NET 8 -> 10).

Before starting, inspect the solution:
```bash
dotnet --list-sdks                         # need a 10.0.x SDK for PR 2
dotnet list package                        # look at the "Resolved" column
dotnet list package --vulnerable --include-transitive
grep -rn "TargetFramework" --include=*.csproj .
```

---

## Key facts (read first)

- `Version="8"` in NuGet means **>= 8.0.0**, and NuGet resolves the **lowest**
  match -> exactly 8.0.0. Always pin full versions (e.g. `8.0.33`).
- Microsoft packages tied to the framework must match its major version:
  `Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.*`,
  `Microsoft.Extensions.*` (framework-shipped ones). EF Core 10 = .NET 10.
- Third-party packages (StackExchange.Redis, Serilog, MediatR, ...) have their
  own versioning; upgrade them only if needed, in a separate PR.
- .NET 10 is LTS. Do not pick a preview/RC SDK for production.
- Always check the latest patch versions on nuget.org at upgrade time; never
  guess versions from memory.

---

## PR 1 – Centralize versions

### 1. `Directory.Build.props` (solution root)
Shared MSBuild properties for every project below it.
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Fail the build on high / critical NuGet vulnerabilities -->
    <WarningsAsErrors>$(WarningsAsErrors);NU1903;NU1904</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

### 2. `Directory.Packages.props` (solution root) – Central Package Management
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- Let us pin vulnerable transitive packages from here -->
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>

    <!-- One variable per framework-bound family: upgrade = edit one line -->
    <EfCoreVersion>8.0.x</EfCoreVersion>
    <AspNetCoreVersion>8.0.x</AspNetCoreVersion>
  </PropertyGroup>

  <ItemGroup Label="Framework-bound">
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design"    Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Tools"     Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing"        Version="$(AspNetCoreVersion)" />
  </ItemGroup>

  <ItemGroup Label="Third-party">
    <!-- copy exact versions from the current csproj files -->
    <PackageVersion Include="StackExchange.Redis" Version="x.y.z" />
  </ItemGroup>

  <ItemGroup Label="Testing">
    <PackageVersion Include="Microsoft.NET.Test.Sdk"    Version="x.y.z" />
    <PackageVersion Include="xunit"                     Version="x.y.z" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="x.y.z" />
    <PackageVersion Include="coverlet.collector"        Version="x.y.z" />
  </ItemGroup>

  <ItemGroup Label="Security pins (transitive)">
    <!-- Example: pin a vulnerable transitive dependency to its patched version -->
    <!-- <PackageVersion Include="System.Security.Cryptography.Pkcs" Version="x.y.z" /> -->
  </ItemGroup>
</Project>
```
Replace every `x` with real versions (latest patch of the current major).

### 3. Clean every `.csproj`
- Remove `TargetFramework`, `Nullable`, `ImplicitUsings`.
- Remove `Version="..."` from every `<PackageReference>`.
- Keep `RootNamespace`, `ProjectReference`, `IncludeAssets/PrivateAssets`.
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
  </ItemGroup>
</Project>
```
A csproj that becomes almost empty is correct.

### 4. Pin dotnet-ef in the repo
```bash
dotnet new tool-manifest            # creates .config/dotnet-tools.json
dotnet tool install dotnet-ef --version <same as EfCoreVersion>
```

### 5. Dockerfile: copy the props files before restore
```dockerfile
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/App.API/App.API.csproj", "src/App.API/"]
# ...other csproj files the API references (not test projects)
RUN dotnet restore "src/App.API/App.API.csproj"
```

### 6. Verify
```bash
dotnet restore && dotnet build && dotnet test
dotnet list package                 # Resolved = pinned versions
dotnet msbuild src/App.API -getProperty:TargetFramework
```
Commit: `build: centralize target framework and package versions`

---

## PR 2 – Upgrade .NET 8 -> .NET 10

| File | Change |
|---|---|
| Directory.Build.props | `<TargetFramework>net10.0</TargetFramework>` |
| Directory.Packages.props | `EfCoreVersion` / `AspNetCoreVersion` -> latest `10.0.x` |
| .config/dotnet-tools.json | `dotnet tool update dotnet-ef --version <10.0.x>` |
| Dockerfile | `sdk:8.0` -> `sdk:10.0`, `aspnet:8.0` -> `aspnet:10.0` |
| CI (GitHub Actions) | `dotnet-version: '10.0.x'` |
| global.json (if present) | SDK version -> 10.0.x |

Then:
```bash
dotnet restore && dotnet build && dotnet test
dotnet list package --vulnerable --include-transitive
dotnet ef migrations has-pending-model-changes \
  --project src/App.Infrastructure --startup-project src/App.API
docker compose up --build            # smoke test the containers
```
Commit: `build: upgrade to .NET 10 and EF Core 10`

---

## Known breaking points 8 -> 10

- **C# 14 first-class Span**: `byte[] -> ReadOnlySpan<byte>` is now an implicit
  conversion. Types with implicit conversions to both `string` and `byte[]`
  (e.g. `RedisValue`) can cause `CS0121 ambiguous call`, e.g.
  `JsonSerializer.Deserialize<T>(redisValue)`.
  Fix: cast explicitly: `JsonSerializer.Deserialize<T>((string)redisValue!)`.
- **Container images**: .NET 10 default images are Ubuntu 24.04 (no Debian).
  Tags like `8.0-bookworm-slim` must change; `apt-get` still works.
- **Swashbuckle / OpenAPI**: Swashbuckle major versions for .NET 10 depend on
  Microsoft.OpenApi 2.x with breaking changes. Upgrade it in its own PR, or
  move to the built-in `Microsoft.AspNetCore.OpenApi`.
- **dotnet-ef mismatch warning**: tool version must match EF Core version.
- **Transitive vulnerabilities (NU1902/NU1903)**: find the chain with
  `dotnet nuget why <project> <package>`, then pin the patched version in
  `Directory.Packages.props` (transitive pinning is enabled).
- **OS-level CVEs in the runtime image** (found by Trivy): add before `USER`:
  ```dockerfile
  RUN apt-get update && apt-get upgrade -y --no-install-recommends \
      && rm -rf /var/lib/apt/lists/*
  ```

## Common errors

| Error | Cause | Fix |
|---|---|---|
| NU1008 | `Version` still in a csproj PackageReference | Remove it; versions live in Directory.Packages.props |
| NU1010 / no version found | Package missing from Directory.Packages.props | Add a `PackageVersion` entry |
| Framework not found (container) | Dockerfile still on 8.0 images | Update both `FROM` lines |
| MSB3202 project not found (Docker restore) | Restoring the .sln without all csproj copied | Restore the API csproj only |
| NETSDK1047 for linux-x64 | `--runtime` used without restoring for it | Drop `--runtime` or restore with `-r` |

## Rules for the assistant
- Never put real versions from memory: check nuget.org first.
- Never mix centralization and upgrade in one PR.
- Never upgrade third-party majors as part of the .NET upgrade.
- After every change: restore, build, test, and show `dotnet list package` output.