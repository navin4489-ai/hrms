using Wintech.Hrms.Api.Common.Tenancy;

namespace Wintech.Hrms.Api.Domain.Chat;

/// <summary>
/// Durable private attachment bytes. The id is the owning message id; access is
/// authorized through that message's conversation before any bytes are read.
/// Keeps chat uploads available when an ephemeral web-service disk is replaced.
/// </summary>
public class ChatAttachmentContent : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public byte[] Content { get; set; } = [];
}
