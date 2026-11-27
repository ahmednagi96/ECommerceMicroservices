using System.Security.Claims;

namespace BuildingBlocks.Authentication;

public static class ClaimsPrincipalExtensions
{
    // The user id from the token ("sub"). Only call it behind [Authorize].
    public static string GetRequiredUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub")
        ?? throw new InvalidOperationException("No 'sub' claim. Is [Authorize] missing on this endpoint?");
}