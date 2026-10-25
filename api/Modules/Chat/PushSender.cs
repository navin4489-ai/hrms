using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Chat;

namespace Wintech.Hrms.Api.Modules.Chat;

/// <summary>
/// Sends FCM push notifications (HTTP v1 API) for chat messages and incoming calls so users are
/// alerted when the app is backgrounded or closed. No-ops gracefully when a service account is not
/// configured, so the rest of chat/calling keeps working.
/// <para>
/// Configure with a Firebase service-account JSON via <c>Fcm:ServiceAccountJson</c> (the whole JSON
/// as a string) or <c>Fcm:ServiceAccountPath</c> (path to the file). Project id is read from it.
/// </para>
/// </summary>
public interface IPushSender : IAppService
{
    Task SendMessageAsync(Guid recipientEmployeeId, string senderName, string preview,
        Guid conversationId, CancellationToken cancellationToken = default);

    Task SendIncomingCallAsync(Guid recipientEmployeeId, string callerName, string callerEmployeeId,
        string callId, string kind, CancellationToken cancellationToken = default);
}

public sealed class FcmPushSender : IPushSender
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FcmPushSender> _logger;

    // Cached OAuth token (valid ~1h); refreshed on demand.
    private static string? _cachedToken;
    private static DateTime _tokenExpiresUtc;
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    public FcmPushSender(AppDbContext db, IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<FcmPushSender> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private sealed record ServiceAccount(string ClientEmail, string PrivateKey, string ProjectId, string TokenUri);

    private ServiceAccount? ResolveServiceAccount()
    {
        var json = _config["Fcm:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(json))
        {
            var path = _config["Fcm:ServiceAccountPath"];
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                json = File.ReadAllText(path);
        }
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ServiceAccount(
                root.GetProperty("client_email").GetString()!,
                root.GetProperty("private_key").GetString()!,
                root.GetProperty("project_id").GetString()!,
                root.TryGetProperty("token_uri", out var t) ? t.GetString()! : "https://oauth2.googleapis.com/token");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid FCM service account JSON; push disabled.");
            return null;
        }
    }

    public Task SendMessageAsync(Guid recipientEmployeeId, string senderName, string preview,
        Guid conversationId, CancellationToken cancellationToken = default)
        => SendAsync(recipientEmployeeId,
            withNotification: true, title: senderName, body: preview,
            data: new Dictionary<string, string>
            {
                ["type"] = "message",
                ["conversationId"] = conversationId.ToString(),
                ["title"] = senderName,
                ["body"] = preview
            },
            highPriority: false, cancellationToken);

    public Task SendIncomingCallAsync(Guid recipientEmployeeId, string callerName, string callerEmployeeId,
        string callId, string kind, CancellationToken cancellationToken = default)
        => SendAsync(recipientEmployeeId,
            withNotification: false, title: null, body: null,
            data: new Dictionary<string, string>
            {
                ["type"] = "call",
                ["callId"] = callId,
                ["fromEmployeeId"] = callerEmployeeId,
                ["fromName"] = callerName,
                ["kind"] = kind
            },
            highPriority: true, cancellationToken);

    private async Task SendAsync(Guid recipientEmployeeId, bool withNotification, string? title, string? body,
        Dictionary<string, string> data, bool highPriority, CancellationToken cancellationToken)
    {
        var sa = ResolveServiceAccount();
        if (sa is null) return; // push not configured — rely on SignalR

        var tokens = await _db.Set<DeviceToken>().AsNoTracking()
            .Where(t => t.EmployeeId == recipientEmployeeId)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0) return;

        string accessToken;
        try { accessToken = await GetAccessTokenAsync(sa, cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "FCM token exchange failed."); return; }

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var url = $"https://fcm.googleapis.com/v1/projects/{sa.ProjectId}/messages:send";

        foreach (var token in tokens)
        {
            try
            {
                var message = new Dictionary<string, object?>
                {
                    ["token"] = token,
                    ["data"] = data,
                    ["android"] = new Dictionary<string, object?>
                    {
                        ["priority"] = highPriority ? "high" : "normal"
                    }
                };
                if (withNotification)
                    message["notification"] = new { title, body };

                var payload = new Dictionary<string, object?> { ["message"] = message };
                var resp = await client.PostAsJsonAsync(url, payload, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("FCM v1 push failed ({Status}) for {Emp}: {Err}",
                        resp.StatusCode, recipientEmployeeId, err);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM v1 push error for employee {Emp}", recipientEmployeeId);
            }
        }
    }

    private async Task<string> GetAccessTokenAsync(ServiceAccount sa, CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
            return _cachedToken;

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
                return _cachedToken;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iss"] = sa.ClientEmail,
                ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
                ["aud"] = sa.TokenUri,
                ["iat"] = now,
                ["exp"] = now + 3600
            }));
            var unsigned = $"{header}.{claims}";

            using var rsa = RSA.Create();
            rsa.ImportFromPem(sa.PrivateKey);
            var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var jwt = $"{unsigned}.{Base64Url(signature)}";

            var client = _httpFactory.CreateClient();
            var resp = await client.PostAsync(sa.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = jwt
            }), cancellationToken);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
            var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            _cachedToken = accessToken;
            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            return accessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
