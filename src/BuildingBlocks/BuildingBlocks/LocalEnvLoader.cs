namespace BuildingBlocks;

public static class LocalEnvLoader
{
    public static void LoadLocalDotEnv()
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
}