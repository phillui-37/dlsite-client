using DlsiteClient.Core.Api;
using DlsiteClient.Platform;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core.Services;

public sealed class AuthSession
{
    public const string CredKeySid = "session_id";
    public const string CredKeyLoginId = "login_id";

    private readonly DlsiteApiClient _api;
    private readonly ICredentialStore _creds;
    private readonly ILogger<AuthSession> _logger;

    public AuthSession(DlsiteApiClient api, ICredentialStore creds, ILogger<AuthSession> logger)
    {
        _api = api;
        _creds = creds;
        _logger = logger;
    }

    public string? LoginId { get; private set; }
    public bool IsLoggedIn => _api.IsLoggedIn;
    public event Action? Changed;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        var sid = await _creds.GetAsync(CredKeySid, ct).ConfigureAwait(false);
        LoginId = await _creds.GetAsync(CredKeyLoginId, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(sid))
        {
            _api.SetSession(sid);
            _logger.LogInformation("Restored session from credential store");
            Changed?.Invoke();
        }
    }

    public async Task ApplyLoginAsync(string sessionId, string? loginId, CancellationToken ct = default)
    {
        _api.LoginWithSessionId(sessionId);
        LoginId = loginId;
        await _creds.SaveAsync(CredKeySid, sessionId, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(loginId))
            await _creds.SaveAsync(CredKeyLoginId, loginId, ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>OIDC id_token exchange (Nest LoginWindow flow).</summary>
    public async Task LoginWithOidcAsync(
        string idToken, string state, string sessionState, CancellationToken ct = default)
    {
        var data = await _api.LoginWithIdTokenAsync(idToken, state, sessionState, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(data?.SessionId))
            throw new InvalidOperationException("Nest login returned no session_id");
        await ApplyLoginAsync(data.SessionId, data.LoginId, ct).ConfigureAwait(false);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _api.LogoutAsync(ct).ConfigureAwait(false);
        await _creds.DeleteAsync(CredKeySid, ct).ConfigureAwait(false);
        await _creds.DeleteAsync(CredKeyLoginId, ct).ConfigureAwait(false);
        LoginId = null;
        Changed?.Invoke();
    }
}
