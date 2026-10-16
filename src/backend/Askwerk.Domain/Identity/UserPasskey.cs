// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Identity;

/// <summary>
/// A passkey (WebAuthn credential) of a user (AWK-16, W4). Verified with fingerprint, face or device PIN, it
/// counts as two-factor, also for owners and admins (Dražan, 2026-10-14). The private key never leaves the device.
/// </summary>
public sealed class UserPasskey
{
    /// <summary>Credential id chosen by the authenticator.</summary>
    public required byte[] CredentialId { get; init; }

    /// <summary>The owner of the passkey.</summary>
    public required Guid UserId { get; init; }

    /// <summary>COSE public key.</summary>
    public required byte[] PublicKey { get; set; }

    /// <summary>Signature counter reported by the authenticator.</summary>
    public long SignCount { get; set; }

    /// <summary>How the browser can reach the authenticator (internal, usb, hybrid, …).</summary>
    public string[] Transports { get; set; } = [];

    /// <summary>Whether the user was verified (fingerprint, face, PIN) when the passkey was used last.</summary>
    public bool IsUserVerified { get; set; }

    /// <summary>Whether the passkey may be synced to other devices.</summary>
    public bool IsBackupEligible { get; set; }

    /// <summary>Whether the passkey is synced.</summary>
    public bool IsBackedUp { get; set; }

    /// <summary>Attestation object from registration.</summary>
    public byte[] AttestationObject { get; set; } = [];

    /// <summary>Client data JSON from registration.</summary>
    public byte[] ClientDataJson { get; set; } = [];

    /// <summary>Name the user gave it, e.g. "Pixel 7".</summary>
    public string? Name { get; set; }

    /// <summary>When it was added (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it was last used to sign in (UTC).</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
