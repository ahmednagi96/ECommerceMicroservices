using System.Net.Http.Headers;

namespace Web.Bff.Clients;

// Copies the user's "Authorization: Bearer ..." header to the outgoing request
public sealed class ForwardAccessTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrEmpty(authorization))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);

        return base.SendAsync(request, ct);
    }
}