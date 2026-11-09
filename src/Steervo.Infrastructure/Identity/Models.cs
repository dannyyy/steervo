namespace Steervo.Infrastructure.Identity;

public sealed class AppUser : Microsoft.AspNetCore.Identity.IdentityUser
{
    public string Name { get; set; } = "";
}
public sealed class Installation { public int Id { get; set; } = 1; public string PrimaryUserId { get; set; } = ""; }
public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
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
public sealed class MailReservation { public Guid Id { get; set; } = Guid.NewGuid(); public DateTimeOffset CreatedAt { get; set; } }
