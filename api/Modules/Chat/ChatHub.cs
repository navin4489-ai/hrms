using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Wintech.Hrms.Api.Modules.Chat;

/// <summary>
/// Real-time transport for chat and calling. Clients receive:
/// <list type="bullet">
///   <item><c>message</c> — a new chat message (ChatMessageDto),</item>
///   <item><c>conversationUpdated</c> — conversation list should refresh,</item>
///   <item><c>callOffer</c>/<c>callAnswer</c>/<c>callIce</c> — WebRTC signaling payloads,</item>
///   <item><c>callState</c> — incoming/declined/ended/cancelled call lifecycle.</item>
/// </list>
/// Signaling is relayed per-employee; media is peer-to-peer (WebRTC) and never touches the server.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    /// <summary>employeeId -> set of active connection ids (multiple devices/tabs).</summary>
    private static readonly ConcurrentDictionary<string, HashSet<string>> Connections = new();

    private string? EmployeeId => Context.User?.FindFirst("emp")?.Value;

    public override Task OnConnectedAsync()
    {
        var emp = EmployeeId;
        if (!string.IsNullOrEmpty(emp) && emp != Guid.Empty.ToString())
        {
            var set = Connections.GetOrAdd(emp, _ => new HashSet<string>());
            lock (set) set.Add(Context.ConnectionId);
        }
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var emp = EmployeeId;
        if (!string.IsNullOrEmpty(emp) && Connections.TryGetValue(emp, out var set))
        {
            lock (set)
            {
                set.Remove(Context.ConnectionId);
                if (set.Count == 0) Connections.TryRemove(emp, out _);
            }
        }
        return base.OnDisconnectedAsync(exception);
    }

    // ---- WebRTC signaling relay (client -> server -> target employee) ----

    public Task SendOffer(string targetEmployeeId, string callId, string kind, object sdp)
        => SendToEmployee(targetEmployeeId, "callOffer", new { callId, fromEmployeeId = EmployeeId, kind, sdp });

    public Task SendAnswer(string targetEmployeeId, string callId, object sdp)
        => SendToEmployee(targetEmployeeId, "callAnswer", new { callId, fromEmployeeId = EmployeeId, sdp });

    public Task SendIceCandidate(string targetEmployeeId, string callId, object candidate)
        => SendToEmployee(targetEmployeeId, "callIce", new { callId, fromEmployeeId = EmployeeId, candidate });

    public Task SignalCallState(string targetEmployeeId, string callId, string state)
        => SendToEmployee(targetEmployeeId, "callState", new { callId, fromEmployeeId = EmployeeId, state });

    private Task SendToEmployee(string employeeId, string method, object payload)
    {
        if (Connections.TryGetValue(employeeId, out var set))
        {
            string[] ids;
            lock (set) ids = set.ToArray();
            if (ids.Length > 0) return Clients.Clients(ids).SendAsync(method, payload);
        }
        return Task.CompletedTask;
    }

    // ---- server-side push helpers (used by ChatService via IHubContext) ----

    public static IReadOnlyCollection<string> ConnectionsFor(string employeeId)
        => Connections.TryGetValue(employeeId, out var set)
            ? set.ToArray()
            : Array.Empty<string>();
}
