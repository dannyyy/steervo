using Microsoft.AspNetCore.Identity;

namespace Steervo.Infrastructure.Identity;

/// <summary>
/// An account. The inherited normalized e-mail address is the unique sign-in name.
/// </summary>
public sealed class AppUser : IdentityUser
{
    public string Name { get; set; } = "";
}

/// <summary>
/// The single row that marks the installation as set up and names its primary account.
/// </summary>
public sealed class Installation
{
    public int Id { get; set; } = 1;

    public string PrimaryUserId { get; set; } = "";
}

/// <summary>
/// Permission for one e-mail address to create an account on first sign-in.
/// </summary>
public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = "";

    public string Name { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
}

/// <summary>
/// A one-time sign-in code sent by e-mail, stored encrypted together with its usage state.
/// </summary>
public sealed class EmailChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = "";

    public string Name { get; set; } = "";

    public string ProtectedCode { get; set; } = "";

    public bool IsSetup { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public int Attempts { get; set; }

    public bool Delivered { get; set; }
}

/// <summary>
/// One sent (or attempted) e-mail, counted against the hourly and daily sending limits.
/// </summary>
public sealed class MailReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }
}
