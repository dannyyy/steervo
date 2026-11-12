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
            b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.Currency).HasMaxLength(3);
            b.ToTable(t => t.HasCheckConstraint("ValidEvaluationCurrency", "\"Currency\" IS NULL OR \"Currency\" ~ '^[A-Z]{3}$'"));
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Groups).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Shares).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Candidates).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Offers).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Costs).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Scenarios).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CalculationSettings).WithOne().HasForeignKey<CalculationSettings>(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CostPlanning).WithOne().HasForeignKey<CostPlanning>(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.OfferAssessments).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Assessments).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<CriterionGroup>(b => { b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.Id }); b.Property(x => x.Name).HasMaxLength(160); });
        model.Entity<Criterion>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.Id }); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Unit).HasMaxLength(30);
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
        model.Entity<Candidate>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.Id });
            b.Property(x => x.Brand).HasMaxLength(100); b.Property(x => x.Model).HasMaxLength(160); b.Property(x => x.Variant).HasMaxLength(160);
            b.Property(x => x.Notes).HasMaxLength(4000); b.Property(x => x.Links).HasMaxLength(12000); b.Property(x => x.StatusReason).HasMaxLength(2000);
            b.Property(x => x.Condition).HasConversion<string>(); b.Property(x => x.Status).HasConversion<string>();
            b.Ignore(x => x.DisplayName); b.Ignore(x => x.SourceLinks);
            b.ComplexProperty(x => x.Specifications, specifications => {
                specifications.Property(x => x.Propulsion).HasConversion<string>();
                specifications.Property(x => x.Drive).HasConversion<string>();
            });
            b.ToTable(t => {
                t.HasCheckConstraint("ValidCandidateCondition", "\"Condition\" IN ('Unknown','New','Used')");
                t.HasCheckConstraint("ValidCandidateStatus", "\"Status\" IN ('Watching','Alternative','Favorite','Deferred','Excluded')");
                t.HasCheckConstraint("CandidateStatusReason", "\"Status\" NOT IN ('Deferred','Excluded') OR length(btrim(\"StatusReason\")) > 0");
                t.HasCheckConstraint("ValidCandidateYear", "\"Year\" IS NULL OR \"Year\" BETWEEN 1 AND 9999");
                t.HasCheckConstraint("ValidCandidateMileage", "\"Mileage\" IS NULL OR \"Mileage\" >= 0");
            });
        });
        model.Entity<Offer>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.Id });
            b.Ignore(x => x.Latest);
            b.HasOne<Candidate>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.CandidateId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Revisions).WithOne().HasForeignKey(x => new { x.EvaluationId, x.OfferId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Revisions).HasField("revisions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        model.Entity<OfferRevision>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.OfferId, x.Number });
            b.Property(x => x.ChangeNote).HasMaxLength(2000);
            b.ComplexProperty(x => x.Terms, terms => {
                terms.Property(x => x.Name).HasMaxLength(160); terms.Property(x => x.Provider).HasMaxLength(200); terms.Property(x => x.Lender).HasMaxLength(200);
                terms.Property(x => x.VehicleDescription).HasMaxLength(400); terms.Property(x => x.DeliveryNotes).HasMaxLength(1000); terms.Property(x => x.Notes).HasMaxLength(4000);
                terms.Property(x => x.Kind).HasConversion<string>(); terms.Property(x => x.Commitment).HasConversion<string>();
            });
            b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Lines).HasField("lines").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidOfferRevisionNumber", "\"Number\" > 0");
                t.HasCheckConstraint("ValidOfferKind", "\"Terms_Kind\" IN ('Unknown','Purchase','Leasing','Financing')");
                t.HasCheckConstraint("ValidOfferCommitment", "\"Terms_Commitment\" IN ('Requested','NonBinding','Binding')");
            });
        });
        model.Entity<OfferLine>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasIndex(x => new { x.RevisionId, x.Position }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(300); b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.Kind).HasConversion<string>(); b.Property(x => x.PriceKind).HasConversion<string>();
            b.Property(x => x.Effect).HasConversion<string>(); b.Property(x => x.CostCategory).HasConversion<string>();
            b.ToTable(t => {
                t.HasCheckConstraint("ValidOfferLinePrice", "(\"PriceKind\" = 'Amount' AND \"Amount\" IS NOT NULL AND \"Amount\" >= 0) OR (\"PriceKind\" IN ('Unknown','Included') AND \"Amount\" IS NULL)");
                t.HasCheckConstraint("ValidOfferLineKind", "\"Kind\" IN ('Accessory','Fee','Benefit','Equipment')");
            });
        });
        model.Entity<OfferAssessment>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasIndex(x => new { x.OfferId, x.CriterionId }).IsUnique();
            b.Property(x => x.Source).HasConversion<string>(); b.Property(x => x.Notes).HasMaxLength(4000); b.Ignore(x => x.HasValue);
            b.HasOne<OfferRevision>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.OfferId, x.RevisionNumber }).HasPrincipalKey(x => new { x.EvaluationId, x.OfferId, x.Number }).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Criterion>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.CriterionId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidOfferAssessmentRating", "\"RatingValue\" IS NULL OR \"RatingValue\" BETWEEN 1 AND 5");
                t.HasCheckConstraint("SingleOfferAssessmentValue", "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END) <= 1");
                t.HasCheckConstraint("ValidOfferAssessmentSource", "\"Source\" IN ('Unknown','Manufacturer','Offer','Inspection','TestDrive','Research')");
            });
        });
        model.Entity<Assessment>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasIndex(x => new { x.CandidateId, x.CriterionId }).IsUnique();
            b.Property(x => x.Source).HasConversion<string>(); b.Property(x => x.Notes).HasMaxLength(4000); b.Ignore(x => x.HasValue);
            b.HasOne<Candidate>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.CandidateId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Criterion>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.CriterionId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidAssessmentRating", "\"RatingValue\" IS NULL OR \"RatingValue\" BETWEEN 1 AND 5");
                t.HasCheckConstraint("SingleAssessmentValue", "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END) <= 1");
                t.HasCheckConstraint("ValidAssessmentSource", "\"Source\" IN ('Unknown','Manufacturer','Offer','Inspection','TestDrive','Research')");
            });
        });
        model.Entity<OfferDocument>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.OfferId, x.Id });
            b.Property(x => x.Name).HasMaxLength(160); b.Ignore(x => x.Latest);
            b.HasOne<Offer>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.OfferId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Revisions).WithOne().HasForeignKey(x => new { x.EvaluationId, x.OfferId, x.DocumentId }).HasPrincipalKey(x => new { x.EvaluationId, x.OfferId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Revisions).HasField("revisions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        model.Entity<DocumentRevision>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasIndex(x => new { x.DocumentId, x.Number }).IsUnique();
            b.Property(x => x.Note).HasMaxLength(2000);
            b.ComplexProperty(x => x.File, file => { file.Property(x => x.FileName).HasMaxLength(200); file.Property(x => x.Sha256).HasMaxLength(64); });
            b.HasOne<OfferRevision>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.OfferId, x.OfferRevisionNumber }).HasPrincipalKey(x => new { x.EvaluationId, x.OfferId, x.Number }).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidDocumentNumber", "\"Number\" BETWEEN 1 AND 100");
                t.HasCheckConstraint("ValidDocumentFile", "\"File_Length\" BETWEEN 1 AND 26214400 AND \"File_Sha256\" ~ '^[0-9a-f]{64}$'");
            });
        });
        model.Entity<Steervo.Infrastructure.Documents.PendingDocumentDeletion>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasIndex(x => x.AvailableAt);
        });
        model.Entity<CostPlanning>(b => {
            b.HasKey(x => x.EvaluationId); b.Property(x => x.EvaluationId).ValueGeneratedNever();
            b.ToTable(t => t.HasCheckConstraint("ValidCostPlanning", "\"Months\" BETWEEN 1 AND 1200 AND EXTRACT(DAY FROM \"Start\") = 1 AND EXTRACT(YEAR FROM \"Start\") BETWEEN 1900 AND 9899"));
        });
        model.Entity<CostItem>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Notes).HasMaxLength(2000);
            b.HasAlternateKey(x => new { x.EvaluationId, x.Id });
            b.Property(x => x.Category).HasConversion<string>(); b.Property(x => x.Frequency).HasConversion<string>(); b.Property(x => x.Direction).HasConversion<string>();
            b.HasOne<Candidate>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.CandidateId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidCostPeriod", "\"StartMonth\" BETWEEN 1 AND 1200 AND (\"EndMonth\" IS NULL OR \"EndMonth\" BETWEEN \"StartMonth\" AND 1200)");
                t.HasCheckConstraint("ValidCostAmount", "\"Amount\" IS NULL OR (\"Amount\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Amount\", 2) = \"Amount\")");
                t.HasCheckConstraint("ValidCostFrequency", "\"Frequency\" IN ('Once','Monthly','Yearly','Included') AND (\"Frequency\" <> 'Once' OR \"EndMonth\" IS NULL)");
                t.HasCheckConstraint("ValidCostDirection", "\"Direction\" IN ('Expense','Income') AND (\"Frequency\" <> 'Included' OR (\"Amount\" IS NULL AND \"Direction\" = 'Expense'))");
                t.HasCheckConstraint("ValidCostCategory", "\"Category\" IN ('Service','Tires','PublicCharging','HomeCharging','Fuel','Insurance','Tax','Parking','Other')");
            });
        });
        model.Entity<CalculationSettings>(b => {
            b.HasKey(x => x.EvaluationId); b.Property(x => x.EvaluationId).ValueGeneratedNever();
            b.ToTable(t => t.HasCheckConstraint("ValidAnnualReturn", "\"AnnualReturnPercent\" BETWEEN -99 AND 100"));
        });
        model.Entity<Scenario>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.EvaluationId, x.Id });
            b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Notes).HasMaxLength(4000);
            b.ComplexProperty(x => x.Terms, terms => terms.Property(x => x.Mode).HasConversion<string>());
            b.HasOne<OfferRevision>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.OfferId, x.RevisionNumber }).HasPrincipalKey(x => new { x.EvaluationId, x.OfferId, x.Number }).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Residuals).WithOne().HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.CostAdjustments).WithOne().HasForeignKey(x => new { x.EvaluationId, x.ScenarioId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidAcquisitionMode", "\"Terms_Mode\" IN ('Purchase','LeaseReturn','LeaseBuyout','Financing')");
                t.HasCheckConstraint("ValidScenarioTerm", "\"Terms_TermMonths\" IS NULL OR \"Terms_TermMonths\" BETWEEN 1 AND 1200");
                t.HasCheckConstraint("ValidScenarioLoan", "(\"Terms_LoanInterestPercent\" IS NULL OR \"Terms_LoanInterestPercent\" BETWEEN 0 AND 100) AND (\"Terms_Mode\" <> 'Financing' OR \"Terms_LoanAmount\" IS NULL OR \"Terms_PurchasePrice\" IS NULL OR \"Terms_LoanAmount\" <= \"Terms_PurchasePrice\")");
                foreach (var field in new[] { "PurchasePrice", "InitialFees", "DownPayment", "MonthlyPayment", "FinalPayment", "LoanAmount" })
                    t.HasCheckConstraint("ValidScenario" + field, $"\"Terms_{field}\" IS NULL OR (\"Terms_{field}\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_{field}\", 2) = \"Terms_{field}\")");
                foreach (var field in new[] { "AnnualKilometres", "IncludedKilometresPerYear" })
                    t.HasCheckConstraint("ValidScenario" + field, $"\"Terms_{field}\" IS NULL OR \"Terms_{field}\" BETWEEN 0 AND 1000000");
                foreach (var field in new[] { "ExcessKilometrePrice", "UnusedKilometreCredit" })
                    t.HasCheckConstraint("ValidScenario" + field, $"\"Terms_{field}\" IS NULL OR \"Terms_{field}\" BETWEEN 0 AND 10000");
            });
        });
        model.Entity<ResidualCheckpoint>(b => {
            b.HasKey(x => new { x.ScenarioId, x.Month }); b.Property(x => x.Notes).HasMaxLength(2000);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidResidualMonth", "\"Month\" BETWEEN 1 AND 1200");
                t.HasCheckConstraint("OrderedResidualBand", "(\"Low\" IS NULL OR \"Expected\" IS NULL OR \"Low\" <= \"Expected\") AND (\"Expected\" IS NULL OR \"High\" IS NULL OR \"Expected\" <= \"High\") AND (\"Low\" IS NULL OR \"High\" IS NULL OR \"Low\" <= \"High\")");
                foreach (var field in new[] { "Low", "Expected", "High" })
                    t.HasCheckConstraint("ValidResidual" + field, $"\"{field}\" IS NULL OR (\"{field}\" BETWEEN 0 AND 1000000000000 AND ROUND(\"{field}\", 2) = \"{field}\")");
            });
        });
        model.Entity<ScenarioCostAdjustment>(b => {
            b.HasKey(x => new { x.ScenarioId, x.CostId }); b.Property(x => x.Notes).HasMaxLength(2000);
            b.HasOne<CostItem>().WithMany().HasForeignKey(x => new { x.EvaluationId, x.CostId }).HasPrincipalKey(x => new { x.EvaluationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => {
                t.HasCheckConstraint("ValidAdjustmentPeriod", "\"StartMonth\" BETWEEN 1 AND 1200 AND (\"EndMonth\" IS NULL OR \"EndMonth\" BETWEEN \"StartMonth\" AND 1200)");
                t.HasCheckConstraint("ValidAdjustmentAmount", "\"Amount\" IS NULL OR (\"Amount\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Amount\", 2) = \"Amount\")");
            });
        });
    }
}
