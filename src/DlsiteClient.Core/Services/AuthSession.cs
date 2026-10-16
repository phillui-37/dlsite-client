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
    private readonly object _gate = new();
    private int _invalidating;

    public AuthSession(DlsiteApiClient api, ICredentialStore creds, ILogger<AuthSession> logger)
    {
        _api = api;
        _creds = creds;
        _logger = logger;
    }

    public string? LoginId { get; private set; }
    public bool IsLoggedIn => _api.IsLoggedIn;

    /// <summary>True after Nest rejected the SID until the user signs in again.</summary>
    public bool NeedsRelogin { get; private set; }

    public event Action? Changed;

    /// <summary>Fired once per invalidation (download queue pauses on this).</summary>
    public event Action? SessionExpired;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        var sid = await _creds.GetAsync(CredKeySid, ct).ConfigureAwait(false);
        LoginId = await _creds.GetAsync(CredKeyLoginId, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(sid))
        {
            _api.SetSession(sid);
            NeedsRelogin = false;
            _logger.LogInformation("Restored session from credential store");
            Changed?.Invoke();
        }
    }

    public async Task ApplyLoginAsync(string sessionId, string? loginId, CancellationToken ct = default)
    {
        await _creds.SaveAsync(CredKeySid, sessionId, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(loginId))
            await _creds.SaveAsync(CredKeyLoginId, loginId, ct).ConfigureAwait(false);
        _api.LoginWithSessionId(sessionId);
        LoginId = loginId;
        NeedsRelogin = false;
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
        NeedsRelogin = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Clear local SID after Nest auth failure. Does not call Nest DELETE (session already dead).
    /// Idempotent under concurrent download/sync failures.
    /// </summary>
    public async Task InvalidateLocalSessionAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _invalidating, 1) == 1)
            return;

        try
        {
            lock (_gate)
            {
                if (!_api.IsLoggedIn && NeedsRelogin)
                    return;
                _api.SetSession(null);
                LoginId = null;
                NeedsRelogin = true;
            }

            try
            {
                await _creds.DeleteAsync(CredKeySid, ct).ConfigureAwait(false);
                await _creds.DeleteAsync(CredKeyLoginId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear credential store after session expiry");
            }

            _logger.LogWarning("Nest session expired; cleared local credentials");
            SessionExpired?.Invoke();
            Changed?.Invoke();
        }
        finally
        {
            Interlocked.Exchange(ref _invalidating, 0);
        }
    }
}
