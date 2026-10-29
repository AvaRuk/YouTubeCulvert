using YoutubeCulvert.Configuration;

namespace YoutubeCulvert.InnerTube;

/// <summary>
/// Default profile selection: an explicit profile wins; videos use VISIONOS
/// (direct URLs without PO token); everything else prefers WEB with a cookie
/// session (SAPISIDHASH) and falls back to TVHTML5 with a bearer token.
/// </summary>
public sealed class AutoClientProfileProvider : IClientProfileProvider
{
    public InnerTubeClientProfile GetFor(InnerTubeRequest request, AuthContext auth)
    {
        if (request.Profile is { } explicitProfile)
            return explicitProfile;

        // Playback goes through VISIONOS even without a session: it returns
        // direct URLs and does not require a PO token. WEB would answer
        // UNPLAYABLE for anonymous player calls.
        if (string.Equals(request.Endpoint, "player", StringComparison.OrdinalIgnoreCase))
            return InnerTubeClientProfile.VisionOs;

        if (request.AuthMode == AuthMode.Anonymous)
            return InnerTubeClientProfile.Web;

        if (request.AuthMode == AuthMode.Bearer && auth.HasBearer)
            return InnerTubeClientProfile.TvHtml5;

        if (auth.HasSapisid)
            return InnerTubeClientProfile.Web;

        if (auth.HasBearer)
            return InnerTubeClientProfile.TvHtml5;

        return InnerTubeClientProfile.Web;
    }
}
