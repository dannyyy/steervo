using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Steervo.Core;
using Steervo.Infrastructure.Documents;
using Steervo.Infrastructure.Identity;

namespace Steervo.Infrastructure.Data;

/// <summary>
/// The single database context: accounts and sign-in data next to the evaluation aggregate.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    private const int EmailLength = 254;
    private const int NameLength = 160;
    private const int NoteLength = 2000;
    private const int LongNoteLength = 4000;

    public DbSet<Installation> Installations => Set<Installation>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<EmailChallenge> Challenges => Set<EmailChallenge>();

    public DbSet<MailReservation> MailReservations => Set<MailReservation>();

    public DbSet<Evaluation> Evaluations => Set<Evaluation>();

    // The order of the entities and of the calls inside each method is the order the migration
    // snapshot was generated from. Keep it when adding to the model.
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        model.Entity<AppUser>(ConfigureUser);
        model.Entity<Installation>(ConfigureInstallation);
        model.Entity<Invitation>(ConfigureInvitation);
        model.Entity<EmailChallenge>(ConfigureEmailChallenge);
        model.Entity<MailReservation>().HasKey(reservation => reservation.Id);

        model.Entity<Evaluation>(ConfigureEvaluation);
        model.Entity<CriterionGroup>(ConfigureCriterionGroup);
        model.Entity<Criterion>(ConfigureCriterion);
        model.Entity<CaseShare>(ConfigureCaseShare);
        model.Entity<Candidate>(ConfigureCandidate);

        model.Entity<Offer>(ConfigureOffer);
        model.Entity<OfferRevision>(ConfigureOfferRevision);
        model.Entity<OfferLine>(ConfigureOfferLine);
        model.Entity<OfferAssessment>(ConfigureOfferAssessment);
        model.Entity<Assessment>(ConfigureAssessment);

        model.Entity<OfferDocument>(ConfigureOfferDocument);
        model.Entity<DocumentRevision>(ConfigureDocumentRevision);
        model.Entity<PendingDocumentDeletion>(ConfigurePendingDocumentDeletion);

        model.Entity<CostPlanning>(ConfigureCostPlanning);
        model.Entity<CostItem>(ConfigureCostItem);
        model.Entity<CalculationSettings>(ConfigureCalculationSettings);

        model.Entity<Scenario>(ConfigureScenario);
        model.Entity<ResidualCheckpoint>(ConfigureResidualCheckpoint);
        model.Entity<ScenarioCostAdjustment>(ConfigureScenarioCostAdjustment);
    }

    private static void ConfigureUser(EntityTypeBuilder<AppUser> entity)
    {
        entity.ToTable("Users");
        entity.HasKey(user => user.Id);
        entity.Property(user => user.Email).HasMaxLength(EmailLength);
        entity.Property(user => user.NormalizedEmail).HasMaxLength(EmailLength);
        entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        entity.Property(user => user.Name).HasMaxLength(NameLength);
    }

    private static void ConfigureInstallation(EntityTypeBuilder<Installation> entity)
    {
        entity.HasKey(installation => installation.Id);

        // The fixed key allows exactly one row: an installation is set up only once.
        entity.ToTable(table => table.HasCheckConstraint("SingleInstallation", "\"Id\" = 1"));

        entity
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(installation => installation.PrimaryUserId);
    }

    private static void ConfigureInvitation(EntityTypeBuilder<Invitation> entity)
    {
        entity.HasKey(invitation => invitation.Id);
        entity.HasIndex(invitation => invitation.Email).IsUnique();
        entity.Property(invitation => invitation.Email).HasMaxLength(EmailLength);
    }

    private static void ConfigureEmailChallenge(EntityTypeBuilder<EmailChallenge> entity)
    {
        entity.HasKey(challenge => challenge.Id);
        entity.HasIndex(challenge => new { challenge.Email, challenge.CreatedAt });
    }

    private static void ConfigureEvaluation(EntityTypeBuilder<Evaluation> entity)
    {
        entity.HasKey(evaluation => evaluation.Id);
        entity.Property(evaluation => evaluation.Id).ValueGeneratedNever();
        entity.Property(evaluation => evaluation.Name).HasMaxLength(NameLength);
        entity.Property(evaluation => evaluation.Purpose).HasMaxLength(NoteLength);

        entity.Property(evaluation => evaluation.Country).HasMaxLength(2);
        entity.ToTable(table =>
            table.HasCheckConstraint(
                "ValidEvaluationCountry",
                "\"Country\" IS NULL OR \"Country\" ~ '^[A-Z]{2}$'"
            )
        );

        entity.Property(evaluation => evaluation.Version).IsConcurrencyToken();

        entity.Property(evaluation => evaluation.Currency).HasMaxLength(3);
        entity.ToTable(table =>
            table.HasCheckConstraint(
                "ValidEvaluationCurrency",
                "\"Currency\" IS NULL OR \"Currency\" ~ '^[A-Z]{3}$'"
            )
        );

        // An owner cannot be deleted while evaluations still belong to them.
        entity
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(evaluation => evaluation.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Everything below is part of the aggregate and disappears with its evaluation.
        entity
            .HasMany(evaluation => evaluation.Groups)
            .WithOne()
            .HasForeignKey(group => group.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Criteria)
            .WithOne()
            .HasForeignKey(criterion => criterion.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Shares)
            .WithOne()
            .HasForeignKey(share => share.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Candidates)
            .WithOne()
            .HasForeignKey(candidate => candidate.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Offers)
            .WithOne()
            .HasForeignKey(offer => offer.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Documents)
            .WithOne()
            .HasForeignKey(document => document.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Costs)
            .WithOne()
            .HasForeignKey(cost => cost.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Scenarios)
            .WithOne()
            .HasForeignKey(scenario => scenario.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasOne(evaluation => evaluation.CalculationSettings)
            .WithOne()
            .HasForeignKey<CalculationSettings>(settings => settings.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasOne(evaluation => evaluation.CostPlanning)
            .WithOne()
            .HasForeignKey<CostPlanning>(planning => planning.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.OfferAssessments)
            .WithOne()
            .HasForeignKey(assessment => assessment.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(evaluation => evaluation.Assessments)
            .WithOne()
            .HasForeignKey(assessment => assessment.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCriterionGroup(EntityTypeBuilder<CriterionGroup> entity)
    {
        entity.HasKey(group => group.Id);
        entity.Property(group => group.Id).ValueGeneratedNever();

        // Composite alternate keys let references prove that both ends share one evaluation.
        entity.HasAlternateKey(group => new { group.EvaluationId, group.Id });
        entity.Property(group => group.Name).HasMaxLength(NameLength);
    }

    private static void ConfigureCriterion(EntityTypeBuilder<Criterion> entity)
    {
        entity.HasKey(criterion => criterion.Id);
        entity.Property(criterion => criterion.Id).ValueGeneratedNever();
        entity.HasAlternateKey(criterion => new { criterion.EvaluationId, criterion.Id });
        entity.Property(criterion => criterion.Name).HasMaxLength(NameLength);
        entity.Property(criterion => criterion.Unit).HasMaxLength(30);
        entity.Property(criterion => criterion.Type).HasConversion<string>();
        entity.Property(criterion => criterion.Priority).HasConversion<string>();
        entity.Property(criterion => criterion.Scope).HasConversion<string>();
        entity.Property(criterion => criterion.Comparison).HasConversion<string>();

        // A group that still holds criteria cannot be removed.
        entity
            .HasOne<CriterionGroup>()
            .WithMany()
            .HasForeignKey(criterion => new { criterion.EvaluationId, criterion.GroupId })
            .HasPrincipalKey(group => new { group.EvaluationId, group.Id })
            .OnDelete(DeleteBehavior.Restrict);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ValidCriterionType",
                "\"Type\" IN ('Boolean','Measurement','Rating')"
            );
            table.HasCheckConstraint(
                "ValidPriority",
                "\"Priority\" IN ('Required','Preferred','Optional')"
            );
            table.HasCheckConstraint(
                "MeasurementHasThreshold",
                "\"Type\" <> 'Measurement' OR (\"Threshold\" IS NOT NULL AND \"Unit\" IS NOT NULL)"
            );
        });
    }

    private static void ConfigureCaseShare(EntityTypeBuilder<CaseShare> entity)
    {
        entity.HasKey(share => new { share.EvaluationId, share.UserId });
        entity.Property(share => share.Access).HasConversion<string>();

        entity
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(share => share.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable(table =>
            table.HasCheckConstraint("ValidShareAccess", "\"Access\" IN ('Reader','Editor')")
        );
    }

    private static void ConfigureCandidate(EntityTypeBuilder<Candidate> entity)
    {
        entity.HasKey(candidate => candidate.Id);
        entity.Property(candidate => candidate.Id).ValueGeneratedNever();
        entity.HasAlternateKey(candidate => new { candidate.EvaluationId, candidate.Id });
        entity.Property(candidate => candidate.Brand).HasMaxLength(100);
        entity.Property(candidate => candidate.Model).HasMaxLength(NameLength);
        entity.Property(candidate => candidate.Variant).HasMaxLength(NameLength);
        entity.Property(candidate => candidate.Notes).HasMaxLength(LongNoteLength);
        entity.Property(candidate => candidate.Links).HasMaxLength(12000);
        entity.Property(candidate => candidate.StatusReason).HasMaxLength(NoteLength);
        entity.Property(candidate => candidate.Condition).HasConversion<string>();
        entity.Property(candidate => candidate.Status).HasConversion<string>();
        entity.Ignore(candidate => candidate.DisplayName);
        entity.Ignore(candidate => candidate.SourceLinks);

        entity.Property(candidate => candidate.Specifications.Propulsion).HasConversion<string>();
        entity.Property(candidate => candidate.Specifications.Drive).HasConversion<string>();

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ValidCandidateCondition",
                "\"Condition\" IN ('Unknown','New','Used')"
            );
            table.HasCheckConstraint(
                "ValidCandidateStatus",
                "\"Status\" IN ('Watching','Alternative','Favorite','Deferred','Excluded')"
            );
            table.HasCheckConstraint(
                "CandidateStatusReason",
                "\"Status\" NOT IN ('Deferred','Excluded') OR length(btrim(\"StatusReason\")) > 0"
            );
            table.HasCheckConstraint(
                "ValidCandidateYear",
                "\"Year\" IS NULL OR \"Year\" BETWEEN 1 AND 9999"
            );
            table.HasCheckConstraint(
                "ValidCandidateMileage",
                "\"Mileage\" IS NULL OR \"Mileage\" >= 0"
            );
        });
    }

    private static void ConfigureOffer(EntityTypeBuilder<Offer> entity)
    {
        entity.HasKey(offer => offer.Id);
        entity.Property(offer => offer.Id).ValueGeneratedNever();
        entity.HasAlternateKey(offer => new { offer.EvaluationId, offer.Id });
        entity.Ignore(offer => offer.Latest);

        entity
            .HasOne<Candidate>()
            .WithMany()
            .HasForeignKey(offer => new { offer.EvaluationId, offer.CandidateId })
            .HasPrincipalKey(candidate => new { candidate.EvaluationId, candidate.Id })
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasMany(offer => offer.Revisions)
            .WithOne()
            .HasForeignKey(revision => new { revision.EvaluationId, revision.OfferId })
            .HasPrincipalKey(offer => new { offer.EvaluationId, offer.Id })
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .Navigation(offer => offer.Revisions)
            .HasField("revisions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureOfferRevision(EntityTypeBuilder<OfferRevision> entity)
    {
        entity.HasKey(revision => revision.Id);
        entity.Property(revision => revision.Id).ValueGeneratedNever();

        // Assessments, documents and scenarios refer to a revision by its number within the offer.
        entity.HasAlternateKey(revision => new
        {
            revision.EvaluationId,
            revision.OfferId,
            revision.Number,
        });
        entity.Property(revision => revision.ChangeNote).HasMaxLength(NoteLength);
        entity.Ignore(revision => revision.Pricing);

        entity.Property(revision => revision.Terms.Name).HasMaxLength(NameLength);
        entity.Property(revision => revision.Terms.Provider).HasMaxLength(200);
        entity.Property(revision => revision.Terms.Lender).HasMaxLength(200);
        entity.Property(revision => revision.Terms.Intermediary).HasMaxLength(200);
        entity.Property(revision => revision.Terms.VehicleDescription).HasMaxLength(400);
        entity.Property(revision => revision.Terms.DeliveryNotes).HasMaxLength(1000);
        entity.Property(revision => revision.Terms.Notes).HasMaxLength(LongNoteLength);
        entity.Property(revision => revision.Terms.Kind).HasConversion<string>();
        entity.Property(revision => revision.Terms.Commitment).HasConversion<string>();

        entity
            .HasMany(revision => revision.Lines)
            .WithOne()
            .HasForeignKey(line => line.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .Navigation(revision => revision.Lines)
            .HasField("lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ValidOfferRevisionNumber", "\"Number\" > 0");
            table.HasCheckConstraint(
                "ValidOfferKind",
                "\"Terms_Kind\" IN ('Unknown','Purchase','Leasing','Financing')"
            );
            table.HasCheckConstraint(
                "ValidOfferCommitment",
                "\"Terms_Commitment\" IN ('Requested','NonBinding','Binding')"
            );
        });
    }

    private static void ConfigureOfferLine(EntityTypeBuilder<OfferLine> entity)
    {
        entity.HasKey(line => line.Id);
        entity.Property(line => line.Id).ValueGeneratedNever();
        entity.HasIndex(line => new { line.RevisionId, line.Position }).IsUnique();
        entity.Property(line => line.Name).HasMaxLength(300);
        entity.Property(line => line.Notes).HasMaxLength(NoteLength);
        entity.Property(line => line.Kind).HasConversion<string>();
        entity.Property(line => line.PriceKind).HasConversion<string>();
        entity.Property(line => line.Effect).HasConversion<string>();
        entity.Property(line => line.CostCategory).HasConversion<string>();
        entity.Property(line => line.Financed).HasDefaultValue(true);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ValidOfferLinePrice",
                "(\"PriceKind\" = 'Amount' AND \"Amount\" IS NOT NULL AND \"Amount\" >= 0) OR (\"PriceKind\" IN ('Unknown','Included') AND \"Amount\" IS NULL)"
            );
            table.HasCheckConstraint(
                "ValidOfferLineKind",
                "\"Kind\" IN ('Accessory','Fee','Benefit','Equipment','Vehicle','Discount')"
            );
            table.HasCheckConstraint(
                "ValidOfferLineValue",
                "\"Value\" IS NULL OR (\"PriceKind\" = 'Included' AND \"Value\" >= 0)"
            );
        });
    }

    private static void ConfigureOfferAssessment(EntityTypeBuilder<OfferAssessment> entity)
    {
        entity.HasKey(assessment => assessment.Id);
        entity.Property(assessment => assessment.Id).ValueGeneratedNever();
        entity
            .HasIndex(assessment => new { assessment.OfferId, assessment.CriterionId })
            .IsUnique();
        entity.Property(assessment => assessment.Source).HasConversion<string>();
        entity.Property(assessment => assessment.Notes).HasMaxLength(LongNoteLength);
        entity.Ignore(assessment => assessment.HasValue);
        entity.Property(assessment => assessment.OptionalAvailable).HasDefaultValue(false);

        entity
            .HasOne<OfferRevision>()
            .WithMany()
            .HasForeignKey(assessment => new
            {
                assessment.EvaluationId,
                assessment.OfferId,
                assessment.RevisionNumber,
            })
            .HasPrincipalKey(revision => new
            {
                revision.EvaluationId,
                revision.OfferId,
                revision.Number,
            })
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasOne<Criterion>()
            .WithMany()
            .HasForeignKey(assessment => new { assessment.EvaluationId, assessment.CriterionId })
            .HasPrincipalKey(criterion => new { criterion.EvaluationId, criterion.Id })
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ValidOfferAssessmentRating",
                "\"RatingValue\" IS NULL OR \"RatingValue\" BETWEEN 1 AND 5"
            );
            table.HasCheckConstraint(
                "SingleOfferAssessmentValue",
                "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"OptionalAvailable\" THEN 1 ELSE 0 END) <= 1"
            );
            table.HasCheckConstraint(
                "ValidOfferAssessmentSource",
                "\"Source\" IN ('Unknown','Manufacturer','Offer','Inspection','TestDrive','Research')"
            );
        });
    }

    private static void ConfigureAssessment(EntityTypeBuilder<Assessment> entity)
    {
        entity.HasKey(assessment => assessment.Id);
        entity.Property(assessment => assessment.Id).ValueGeneratedNever();
        entity
            .HasIndex(assessment => new { assessment.CandidateId, assessment.CriterionId })
            .IsUnique();
        entity.Property(assessment => assessment.Source).HasConversion<string>();
        entity.Property(assessment => assessment.Notes).HasMaxLength(LongNoteLength);
        entity.Ignore(assessment => assessment.HasValue);
        entity.Property(assessment => assessment.OptionalAvailable).HasDefaultValue(false);

        entity
            .HasOne<Candidate>()
            .WithMany()
            .HasForeignKey(assessment => new { assessment.EvaluationId, assessment.CandidateId })
            .HasPrincipalKey(candidate => new { candidate.EvaluationId, candidate.Id })
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasOne<Criterion>()
            .WithMany()
            .HasForeignKey(assessment => new { assessment.EvaluationId, assessment.CriterionId })
            .HasPrincipalKey(criterion => new { criterion.EvaluationId, criterion.Id })
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ValidAssessmentRating",
                "\"RatingValue\" IS NULL OR \"RatingValue\" BETWEEN 1 AND 5"
            );
            table.HasCheckConstraint(
                "SingleAssessmentValue",
                "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"OptionalAvailable\" THEN 1 ELSE 0 END) <= 1"
            );
            table.HasCheckConstraint(
                "ValidAssessmentSource",
                "\"Source\" IN ('Unknown','Manufacturer','Offer','Inspection','TestDrive','Research')"
            );
        });
    }

    private static void ConfigureOfferDocument(EntityTypeBuilder<OfferDocument> entity)
    {
        entity.HasKey(document => document.Id);
        entity.Property(document => document.Id).ValueGeneratedNever();
        entity.HasAlternateKey(document => new
        {
            document.EvaluationId,
            document.OfferId,
            document.Id,
        });
        entity.Property(document => document.Name).HasMaxLength(NameLength);
        entity.Ignore(document => document.Latest);

        entity
            .HasOne<Offer>()
            .WithMany()
            .HasForeignKey(document => new { document.EvaluationId, document.OfferId })
            .HasPrincipalKey(offer => new { offer.EvaluationId, offer.Id })
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasMany(document => document.Revisions)
            .WithOne()
            .HasForeignKey(revision => new
            {
                revision.EvaluationId,
                revision.OfferId,
                revision.DocumentId,
            })
            .HasPrincipalKey(document => new
            {
                document.EvaluationId,
                document.OfferId,
                document.Id,
            })
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .Navigation(document => document.Revisions)
            .HasField("revisions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureDocumentRevision(EntityTypeBuilder<DocumentRevision> entity)
    {
        entity.HasKey(revision => revision.Id);
        entity.Property(revision => revision.Id).ValueGeneratedNever();
        entity.HasIndex(revision => new { revision.DocumentId, revision.Number }).IsUnique();
        entity.Property(revision => revision.Note).HasMaxLength(NoteLength);

        entity.Property(revision => revision.File.FileName).HasMaxLength(200);
        entity.Property(revision => revision.File.Sha256).HasMaxLength(64);

        entity
            .HasOne<OfferRevision>()
            .WithMany()
            .HasForeignKey(revision => new
            {
                revision.EvaluationId,
                revision.OfferId,
                revision.OfferRevisionNumber,
            })
            .HasPrincipalKey(offerRevision => new
            {
                offerRevision.EvaluationId,
                offerRevision.OfferId,
                offerRevision.Number,
            })
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ValidDocumentNumber", "\"Number\" BETWEEN 1 AND 100");
            table.HasCheckConstraint(
                "ValidDocumentFile",
                "\"File_Length\" BETWEEN 1 AND 26214400 AND \"File_Sha256\" ~ '^[0-9a-f]{64}$'"
            );
        });
    }

    private static void ConfigurePendingDocumentDeletion(
        EntityTypeBuilder<PendingDocumentDeletion> entity
    )
    {
        entity.HasKey(deletion => deletion.Id);
        entity.Property(deletion => deletion.Id).ValueGeneratedNever();
        entity.HasIndex(deletion => deletion.AvailableAt);
    }

    private static void ConfigureCostPlanning(EntityTypeBuilder<CostPlanning> entity)
    {
        entity.HasKey(planning => planning.EvaluationId);
        entity.Property(planning => planning.EvaluationId).ValueGeneratedNever();

        entity.ToTable(table =>
            table.HasCheckConstraint(
                "ValidCostPlanning",
                "\"Months\" BETWEEN 1 AND 1200 AND EXTRACT(DAY FROM \"Start\") = 1 AND EXTRACT(YEAR FROM \"Start\") BETWEEN 1900 AND 9899"
            )
        );
    }

    private static void ConfigureCostItem(EntityTypeBuilder<CostItem> entity)
    {
        entity.HasKey(cost => cost.Id);
        entity.Property(cost => cost.Id).ValueGeneratedNever();
        entity.Property(cost => cost.Name).HasMaxLength(200);
        entity.Property(cost => cost.Notes).HasMaxLength(NoteLength);
        entity.HasAlternateKey(cost => new { cost.EvaluationId, cost.Id });
        entity.Property(cost => cost.Category).HasConversion<string>();
        entity.Property(cost => cost.Frequency).HasConversion<string>();
        entity.Property(cost => cost.Direction).HasConversion<string>();

        entity
            .HasOne<Candidate>()
            .WithMany()
            .HasForeignKey(cost => new { cost.EvaluationId, cost.CandidateId })
            .HasPrincipalKey(candidate => new { candidate.EvaluationId, candidate.Id })
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ValidCostPeriod",
                "\"StartMonth\" BETWEEN 1 AND 1200 AND (\"EndMonth\" IS NULL OR \"EndMonth\" BETWEEN \"StartMonth\" AND 1200)"
            );
            table.HasCheckConstraint(
                "ValidCostAmount",
                "\"Amount\" IS NULL OR (\"Amount\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Amount\", 2) = \"Amount\")"
            );
            table.HasCheckConstraint(
                "ValidCostFrequency",
                "\"Frequency\" IN ('Once','Monthly','Yearly','Included') AND (\"Frequency\" <> 'Once' OR \"EndMonth\" IS NULL)"
            );
            table.HasCheckConstraint(
                "ValidCostDirection",
                "\"Direction\" IN ('Expense','Income') AND (\"Frequency\" <> 'Included' OR (\"Amount\" IS NULL AND \"Direction\" = 'Expense'))"
            );
            table.HasCheckConstraint(
                "ValidCostCategory",
                "\"Category\" IN ('Service','Tires','PublicCharging','HomeCharging','Fuel','Insurance','Tax','Parking','Other')"
            );
        });
    }

    private static void ConfigureCalculationSettings(EntityTypeBuilder<CalculationSettings> entity)
    {
        entity.HasKey(settings => settings.EvaluationId);
        entity.Property(settings => settings.EvaluationId).ValueGeneratedNever();

        entity.ToTable(table =>
            table.HasCheckConstraint(
                "ValidAnnualReturn",
                "\"AnnualReturnPercent\" BETWEEN -99 AND 100"
            )
        );
    }

    private static void ConfigureScenario(EntityTypeBuilder<Scenario> entity)
    {
        entity.HasKey(scenario => scenario.Id);
        entity.Property(scenario => scenario.Id).ValueGeneratedNever();
        entity.HasAlternateKey(scenario => new { scenario.EvaluationId, scenario.Id });
        entity.Property(scenario => scenario.Name).HasMaxLength(NameLength);
        entity.Property(scenario => scenario.Notes).HasMaxLength(LongNoteLength);

        entity.Property(scenario => scenario.Terms.Mode).HasConversion<string>();

        entity
            .HasOne<OfferRevision>()
            .WithMany()
            .HasForeignKey(scenario => new
            {
                scenario.EvaluationId,
                scenario.OfferId,
                scenario.RevisionNumber,
            })
            .HasPrincipalKey(revision => new
            {
                revision.EvaluationId,
                revision.OfferId,
                revision.Number,
            })
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasMany(scenario => scenario.Residuals)
            .WithOne()
            .HasForeignKey(checkpoint => checkpoint.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);
        entity
            .HasMany(scenario => scenario.CostAdjustments)
            .WithOne()
            .HasForeignKey(adjustment => new { adjustment.EvaluationId, adjustment.ScenarioId })
            .HasPrincipalKey(scenario => new { scenario.EvaluationId, scenario.Id })
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable(ConfigureScenarioChecks);
    }

    private static void ConfigureScenarioChecks(TableBuilder<Scenario> table)
    {
        table.HasCheckConstraint(
            "ValidAcquisitionMode",
            "\"Terms_Mode\" IN ('Purchase','LeaseReturn','LeaseBuyout','Financing')"
        );
        table.HasCheckConstraint(
            "ValidScenarioTerm",
            "\"Terms_TermMonths\" IS NULL OR \"Terms_TermMonths\" BETWEEN 1 AND 1200"
        );
        table.HasCheckConstraint(
            "ValidScenarioLoan",
            "(\"Terms_LoanInterestPercent\" IS NULL OR \"Terms_LoanInterestPercent\" BETWEEN 0 AND 100) AND (\"Terms_Mode\" <> 'Financing' OR \"Terms_LoanAmount\" IS NULL OR \"Terms_PurchasePrice\" IS NULL OR \"Terms_LoanAmount\" <= \"Terms_PurchasePrice\")"
        );

        // Money amounts: non-negative, bounded and rounded to two decimals.
        string[] moneyFields =
        [
            "PurchasePrice",
            "InitialFees",
            "DownPayment",
            "MonthlyPayment",
            "FinalPayment",
            "LoanAmount",
        ];

        foreach (var field in moneyFields)
        {
            table.HasCheckConstraint(
                "ValidScenario" + field,
                $"\"Terms_{field}\" IS NULL OR (\"Terms_{field}\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_{field}\", 2) = \"Terms_{field}\")"
            );
        }

        string[] distanceFields = ["AnnualKilometres", "IncludedKilometresPerYear"];

        foreach (var field in distanceFields)
        {
            table.HasCheckConstraint(
                "ValidScenario" + field,
                $"\"Terms_{field}\" IS NULL OR \"Terms_{field}\" BETWEEN 0 AND 1000000"
            );
        }

        string[] kilometrePriceFields = ["ExcessKilometrePrice", "UnusedKilometreCredit"];

        foreach (var field in kilometrePriceFields)
        {
            table.HasCheckConstraint(
                "ValidScenario" + field,
                $"\"Terms_{field}\" IS NULL OR \"Terms_{field}\" BETWEEN 0 AND 10000"
            );
        }
    }

    private static void ConfigureResidualCheckpoint(EntityTypeBuilder<ResidualCheckpoint> entity)
    {
        entity.HasKey(checkpoint => new { checkpoint.ScenarioId, checkpoint.Month });
        entity.Property(checkpoint => checkpoint.Notes).HasMaxLength(NoteLength);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ValidResidualMonth", "\"Month\" BETWEEN 1 AND 1200");
            table.HasCheckConstraint(
                "OrderedResidualBand",
                "(\"Low\" IS NULL OR \"Expected\" IS NULL OR \"Low\" <= \"Expected\") AND (\"Expected\" IS NULL OR \"High\" IS NULL OR \"Expected\" <= \"High\") AND (\"Low\" IS NULL OR \"High\" IS NULL OR \"Low\" <= \"High\")"
            );

            string[] bandFields = ["Low", "Expected", "High"];

            foreach (var field in bandFields)
            {
                table.HasCheckConstraint(
                    "ValidResidual" + field,
                    $"\"{field}\" IS NULL OR (\"{field}\" BETWEEN 0 AND 1000000000000 AND ROUND(\"{field}\", 2) = \"{field}\")"
                );
            }
        });
    }

    private static void ConfigureScenarioCostAdjustment(
        EntityTypeBuilder<ScenarioCostAdjustment> entity
    )
    {
        entity.HasKey(adjustment => new { adjustment.ScenarioId, adjustment.CostId });
        entity.Property(adjustment => adjustment.Notes).HasMaxLength(NoteLength);

        entity
            .HasOne<CostItem>()
            .WithMany()
            .HasForeignKey(adjustment => new { adjustment.EvaluationId, adjustment.CostId })
            .HasPrincipalKey(cost => new { cost.EvaluationId, cost.Id })
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ValidAdjustmentPeriod",
                "\"StartMonth\" BETWEEN 1 AND 1200 AND (\"EndMonth\" IS NULL OR \"EndMonth\" BETWEEN \"StartMonth\" AND 1200)"
            );
            table.HasCheckConstraint(
                "ValidAdjustmentAmount",
                "\"Amount\" IS NULL OR (\"Amount\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Amount\", 2) = \"Amount\")"
            );
        });
    }
}
