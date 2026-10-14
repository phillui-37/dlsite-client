namespace DlsiteClient.Core.Api;

/// <summary>Nest rejected the stored SID (HTTP/meta 401 or 403).</summary>
public sealed class NestSessionExpiredException : Exception
{
    public NestSessionExpiredException(string message) : base(message) { }

    public NestSessionExpiredException(string message, Exception inner) : base(message, inner) { }
}
