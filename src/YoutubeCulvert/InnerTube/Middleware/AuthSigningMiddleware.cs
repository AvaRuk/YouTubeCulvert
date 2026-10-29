using YoutubeCulvert.Authentication;

namespace YoutubeCulvert.InnerTube.Middleware;

/// <summary>
/// Resolves <see cref="AuthMode.Auto"/> to the mechanism the current session
/// supports, so later steps (profile selection, signing) see a concrete mode.
/// </summary>
public sealed class AuthSigningMiddleware : IInnerTubeMiddleware
{
    private readonly AuthStateManager _auth;

    public AuthSigningMiddleware(AuthStateManager auth) => _auth = auth;

    public Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        if (request.AuthMode != AuthMode.Auto)
            return next(request, ct);

        var context = _auth.GetAuthContext();

        var mode = context.HasSapisid
            ? AuthMode.Cookie
            : context.HasBearer
                ? AuthMode.Bearer
                : AuthMode.Anonymous;

        return next(request with { AuthMode = mode }, ct);
    }
}
