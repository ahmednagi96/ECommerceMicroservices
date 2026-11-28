namespace BuildingBlocks.Authentication;

// Must match the realm role names in Keycloak exactly (case-sensitive)
public static class AppRoles
{
    public const string Admin = "admin";
    public const string Customer = "customer";
}