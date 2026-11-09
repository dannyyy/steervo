using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Identity;

namespace Steervo.Infrastructure.Mail;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool StartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "steervo@localhost";
    public int HourlyLimit { get; set; } = 50;
    public int DailyLimit { get; set; } = 200;
}

public sealed class SmtpDelivery(AppDbContext db, IOptions<SmtpOptions> options, TimeProvider clock)
{
    public async Task SendAsync(string to, string subject, string body)
    {
        var config = options.Value;
        var now = clock.GetUtcNow();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(78231402)");
            var hourly = await db.MailReservations.CountAsync(x => x.CreatedAt > now.AddHours(-1));
            var daily = await db.MailReservations.CountAsync(x => x.CreatedAt > now.AddDays(-1));
            if (hourly >= config.HourlyLimit || daily >= config.DailyLimit) throw new RuleException("MailQuota");
            await db.MailReservations.Where(x => x.CreatedAt < now.AddDays(-2)).ExecuteDeleteAsync();
            db.MailReservations.Add(new MailReservation { CreatedAt = now });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        // Failed or ambiguous sends retain their quota reservation to avoid exceeding provider limits.
        using var client = new SmtpClient(config.Host, config.Port) { EnableSsl = config.StartTls, Timeout = 15000 };
        if (!string.IsNullOrEmpty(config.Username)) client.Credentials = new NetworkCredential(config.Username, config.Password);
        using var message = new MailMessage(config.From, to, subject, body);
        try { await client.SendMailAsync(message).WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (Exception error) when (error is SmtpException or TimeoutException) { throw new RuleException("MailUnavailable"); }
    }
}
