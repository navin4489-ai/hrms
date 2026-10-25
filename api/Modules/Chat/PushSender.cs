using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Chat;

namespace Wintech.Hrms.Api.Modules.Chat;

/// <summary>
/// Sends FCM push notifications for chat messages and incoming calls so users are alerted when the
/// app is backgrounded or closed (SignalR only works while the app is open). No-ops gracefully when
/// <c>Fcm:ServerKey</c> is not configured, so the rest of chat/calling keeps working.
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

    public FcmPushSender(AppDbContext db, IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<FcmPushSender> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string? ServerKey => _config["Fcm:ServerKey"];

    public async Task SendMessageAsync(Guid recipientEmployeeId, string senderName, string preview,
        Guid conversationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(recipientEmployeeId,
            notification: new { title = senderName, body = preview },
            data: new Dictionary<string, string>
            {
                ["type"] = "message",
                ["conversationId"] = conversationId.ToString(),
                ["title"] = senderName,
                ["body"] = preview
            },
            cancellationToken);
    }

    public async Task SendIncomingCallAsync(Guid recipientEmployeeId, string callerName, string callerEmployeeId,
        string callId, string kind, CancellationToken cancellationToken = default)
    {
        // Data-only (no notification block) so the app shows a full-screen ringing UI itself.
        await SendAsync(recipientEmployeeId,
            notification: null,
            data: new Dictionary<string, string>
            {
                ["type"] = "call",
                ["callId"] = callId,
                ["fromEmployeeId"] = callerEmployeeId,
                ["fromName"] = callerName,
                ["kind"] = kind
            },
            cancellationToken,
            highPriority: true);
    }

    private async Task SendAsync(Guid recipientEmployeeId, object? notification,
        Dictionary<string, string> data, CancellationToken cancellationToken, bool highPriority = false)
    {
        var key = ServerKey;
        if (string.IsNullOrWhiteSpace(key)) return; // push not configured — rely on SignalR

        var tokens = await _db.Set<DeviceToken>().AsNoTracking()
            .Where(t => t.EmployeeId == recipientEmployeeId)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0) return;

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"key={key}");

        foreach (var token in tokens)
        {
            try
            {
                var payload = new Dictionary<string, object?>
                {
                    ["to"] = token,
                    ["priority"] = highPriority ? "high" : "normal",
                    ["data"] = data
                };
                if (notification is not null) payload["notification"] = notification;

                var resp = await client.PostAsJsonAsync("https://fcm.googleapis.com/fcm/send", payload, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                    _logger.LogWarning("FCM push failed ({Status}) for employee {Emp}", resp.StatusCode, recipientEmployeeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM push error for employee {Emp}", recipientEmployeeId);
            }
        }
    }
}
