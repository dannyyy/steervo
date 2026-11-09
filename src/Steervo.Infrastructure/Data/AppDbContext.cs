using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Steervo.Core;
using Steervo.Infrastructure.Identity;

namespace Steervo.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<EmailChallenge> Challenges => Set<EmailChallenge>();
    public DbSet<MailReservation> MailReservations => Set<MailReservation>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<AppUser>(b => {
            b.ToTable("Users"); b.HasKey(x => x.Id); b.Property(x => x.Email).HasMaxLength(254);
            b.Property(x => x.NormalizedEmail).HasMaxLength(254); b.HasIndex(x => x.NormalizedEmail).IsUnique();
            b.Property(x => x.Name).HasMaxLength(160);
        });
        model.Entity<Installation>(b => { b.HasKey(x => x.Id); b.ToTable(t => t.HasCheckConstraint("SingleInstallation", "\"Id\" = 1")); b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PrimaryUserId); });
        model.Entity<Invitation>(b => { b.HasKey(x => x.Id); b.HasIndex(x => x.Email).IsUnique(); b.Property(x => x.Email).HasMaxLength(254); });
        model.Entity<EmailChallenge>(b => { b.HasKey(x => x.Id); b.HasIndex(x => new { x.Email, x.CreatedAt }); });
        model.Entity<MailReservation>().HasKey(x => x.Id);
        model.Entity<Evaluation>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Purpose).HasMaxLength(2000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Groups).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Shares).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<CriterionGroup>(b => { b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.Id }); b.Property(x => x.Name).HasMaxLength(160); });
        model.Entity<Criterion>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Unit).HasMaxLength(30);
            b.Property(x => x.Type).HasConversion<string>(); b.Property(x => x.Priority).HasConversion<string>();
            b.Property(x => x.Scope).HasConversion<string>(); b.Property(x => x.Comparison).HasConversion<string>();
            b.HasOne<CriterionGroup>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.GroupId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidCriterionType", "\"Type\" IN ('Boolean','Measurement','Rating')");
                t.HasCheckConstraint("ValidPriority", "\"Priority\" IN ('Required','Preferred','Optional')");
                t.HasCheckConstraint("MeasurementHasThreshold", "\"Type\" <> 'Measurement' OR (\"Threshold\" IS NOT NULL AND \"Unit\" IS NOT NULL)");
            });
        });
        model.Entity<CaseShare>(b => {
            b.HasKey(x => new { x.EvaluationId, x.UserId }); b.Property(x => x.Access).HasConversion<string>();
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => t.HasCheckConstraint("ValidShareAccess", "\"Access\" IN ('Reader','Editor')"));
        });
    }
}
