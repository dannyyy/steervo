using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Steervo.Application.Accounts;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Mail;
using Xunit;

namespace Steervo.Tests;

/// <summary>All database tests share the local test servers and therefore run one after another.</summary>
[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection;

[Collection("Postgres")]
public sealed class PostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migrations_start_empty_and_setup_is_single_use_under_competing_requests(
        int port
    )
    {
        // A freshly migrated database has no account and no evaluation.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        Assert.False(await fixture.AccountAsync(accounts => accounts.IsInitializedAsync()));
        Assert.Equal(
            0,
            await fixture.RunAsync(provider =>
                provider.GetRequiredService<AppDbContext>().Evaluations.CountAsync()
            )
        );

        // Setup needs the right token.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AccountAsync(accounts => accounts.BeginSetupAsync("bad", "a@example.test", "A"))
        );

        // Two setups are started; when both are confirmed at once, only one may win.
        var first = await fixture.AccountAsync(accounts =>
            accounts.BeginSetupAsync(PostgresFixture.SetupToken, "a@example.test", "A")
        );
        var second = await fixture.AccountAsync(accounts =>
            accounts.BeginSetupAsync(PostgresFixture.SetupToken, "b@example.test", "B")
        );
        var firstCode = fixture.Mail.Codes["A@EXAMPLE.TEST"];
        var secondCode = fixture.Mail.Codes["B@EXAMPLE.TEST"];

        var results = await Task.WhenAll(
            Attempt(() =>
                fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(first, firstCode))
            ),
            Attempt(() =>
                fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(second, secondCode))
            )
        );

        Assert.Single(results, succeeded => succeeded);
        Assert.Equal(
            1,
            await fixture.RunAsync(provider =>
                provider.GetRequiredService<AppDbContext>().Users.CountAsync()
            )
        );

        // Once an account exists, setup is closed for good.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AccountAsync(accounts =>
                accounts.BeginSetupAsync(PostgresFixture.SetupToken, "c@example.test", "C")
            )
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Codes_expire_are_consumed_once_and_have_bounded_attempts(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();

        // A code signs in once and is used up afterwards.
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        var codeId = await fixture.AccountAsync(accounts =>
            accounts.RequestCodeAsync("owner@example.test")
        );
        var code = fixture.Mail.Codes["OWNER@EXAMPLE.TEST"];
        Assert.Equal(
            owner,
            await fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(codeId, code))
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(codeId, code))
        );

        // After five wrong attempts even the right code is refused.
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        codeId = await fixture.AccountAsync(accounts =>
            accounts.RequestCodeAsync("owner@example.test")
        );
        code = fixture.Mail.Codes["OWNER@EXAMPLE.TEST"];
        var wrong = code == "000000" ? "111111" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<RuleException>(() =>
                fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(codeId, wrong))
            );
        }

        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(codeId, code))
        );

        // A code that was never tried expires on its own.
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        codeId = await fixture.AccountAsync(accounts =>
            accounts.RequestCodeAsync("owner@example.test")
        );
        code = fixture.Mail.Codes["OWNER@EXAMPLE.TEST"];
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(codeId, code))
        );

        // An unknown address gets an id like any other, but no mail and no way in.
        var unknown = await fixture.AccountAsync(accounts =>
            accounts.RequestCodeAsync("unknown@example.test")
        );
        Assert.False(fixture.Mail.Codes.ContainsKey("UNKNOWN@EXAMPLE.TEST"));
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AccountAsync(accounts => accounts.VerifyCodeAsync(unknown, "000000"))
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Invitations_are_primary_only_expire_and_do_not_share_records(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var invited = await fixture.InviteAsync(owner, "invited@example.test");

        // An invited user does not see the owner's evaluations.
        var caseId = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Private decision", null, "CH", "EUR")
        );
        Assert.Empty(await fixture.AsAsync(invited, workspace => workspace.ListAsync()));
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(invited, workspace => workspace.ReadAsync(caseId))
        );

        // Only the primary account may invite.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ExecuteAsync(provider =>
                provider
                    .GetRequiredService<IAccountAccess>()
                    .InviteAsync(invited, "third@example.test", "Third")
            )
        );

        // A code requested for an invitation is worthless once the invitation is revoked.
        await fixture.ExecuteAsync(provider =>
            provider
                .GetRequiredService<IAccountAccess>()
                .InviteAsync(owner, "expired@example.test", "Expired")
        );
        var codeId = await fixture.AccountAsync(accounts =>
            accounts.RequestCodeAsync("expired@example.test")
        );
        var invitations = await fixture.AccountAsync(accounts => accounts.InvitationsAsync(owner));
        var invitationId = invitations
            .Single(invitation => invitation.Email == "EXPIRED@EXAMPLE.TEST")
            .Id;
        await fixture.ExecuteAsync(provider =>
            provider.GetRequiredService<IAccountAccess>().RevokeInvitationAsync(owner, invitationId)
        );

        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AccountAsync(accounts =>
                accounts.VerifyCodeAsync(codeId, fixture.Mail.Codes["EXPIRED@EXAMPLE.TEST"])
            )
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Resource_permissions_and_optimistic_concurrency_hold_across_database_connections(
        int port
    )
    {
        // Arrange: an evaluation that belongs to an invited user, not to the primary account.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var editor = await fixture.InviteAsync(owner, "editor@example.test");
        var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var evaluationId = await fixture.AsAsync(
            editor,
            workspace => workspace.CreateAsync("Owned by invited user", null, "CH", "EUR")
        );

        // The primary account has no access to it.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId))
        );

        // A reader cannot edit.
        await fixture.ActAsync(
            editor,
            workspace =>
                workspace.ShareAsync(evaluationId, 1, "reader@example.test", AccessLevel.Reader)
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ActAsync(
                reader,
                workspace =>
                    workspace.EditAsync(
                        evaluationId,
                        2,
                        evaluation => evaluation.AddGroup("Blocked")
                    )
            )
        );

        // Raised to editor, the same user may still neither share nor delete.
        await fixture.ActAsync(
            editor,
            workspace =>
                workspace.ShareAsync(evaluationId, 2, "reader@example.test", AccessLevel.Editor)
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ActAsync(
                reader,
                workspace =>
                    workspace.ShareAsync(evaluationId, 3, "owner@example.test", AccessLevel.Reader)
            )
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ActAsync(reader, workspace => workspace.DeleteAsync(evaluationId, 3))
        );

        // Two writers start from the same version; exactly one of them succeeds.
        var results = await Task.WhenAll(
            Attempt(() =>
                fixture.AsAsync(
                    editor,
                    async workspace =>
                    {
                        await workspace.EditAsync(
                            evaluationId,
                            3,
                            evaluation => evaluation.AddGroup("First writer")
                        );
                        return true;
                    }
                )
            ),
            Attempt(() =>
                fixture.AsAsync(
                    reader,
                    async workspace =>
                    {
                        await workspace.EditAsync(
                            evaluationId,
                            3,
                            evaluation => evaluation.AddGroup("Second writer")
                        );
                        return true;
                    }
                )
            )
        );

        Assert.Single(results, succeeded => succeeded);
        var saved = await fixture.AsAsync(editor, workspace => workspace.ReadAsync(evaluationId));
        Assert.Equal(4, saved.Version);
        Assert.Single(saved.Groups);

        // Removing a group keeps its criterion and only clears the assignment.
        await fixture.ActAsync(
            editor,
            workspace => workspace.EditAsync(evaluationId, 4, AddCriterionToOnlyGroup)
        );
        var criterionId = (
            await fixture.AsAsync(reader, workspace => workspace.ReadAsync(evaluationId))
        )
            .Criteria.Single()
            .Id;
        await fixture.ActAsync(
            reader,
            workspace =>
                workspace.EditAsync(
                    evaluationId,
                    5,
                    evaluation => evaluation.RemoveGroup(evaluation.Groups.Single().Id)
                )
        );

        saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(evaluationId));
        Assert.Equal(criterionId, saved.Criteria.Single().Id);
        Assert.Null(saved.Criteria.Single().GroupId);

        // A revoked share ends the access at once.
        await fixture.ActAsync(editor, workspace => workspace.RevokeAsync(evaluationId, 6, reader));
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(reader, workspace => workspace.ReadAsync(evaluationId))
        );

        // Deleting the evaluation removes its groups and criteria as well.
        await fixture.ActAsync(
            editor,
            workspace =>
                workspace.EditAsync(
                    evaluationId,
                    7,
                    evaluation =>
                        evaluation.Reassign(criterionId, evaluation.AddGroup("Final group"))
                )
        );
        await fixture.ActAsync(editor, workspace => workspace.DeleteAsync(evaluationId, 8));

        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(editor, workspace => workspace.ReadAsync(evaluationId))
        );
        Assert.Equal(
            0,
            await fixture.RunAsync(provider =>
                provider.GetRequiredService<AppDbContext>().Set<Criterion>().CountAsync()
            )
        );
        Assert.Equal(
            0,
            await fixture.RunAsync(provider =>
                provider.GetRequiredService<AppDbContext>().Set<CriterionGroup>().CountAsync()
            )
        );
    }

    private static void AddCriterionToOnlyGroup(Evaluation evaluation)
    {
        evaluation.AddCriterion(
            new(
                "Criterion",
                CriterionType.Boolean,
                Priority.Required,
                CriterionScope.Vehicle,
                evaluation.Groups.Single().Id
            )
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Export_requires_access_and_import_creates_a_private_independent_case_atomically(
        int port
    )
    {
        // Arrange: the owner's evaluation with one group and one measured criterion.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var invited = await fixture.InviteAsync(owner, "reader@example.test");
        var evaluationId = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Export test", "User-entered purpose", "CH", "EUR")
        );
        await fixture.ActAsync(
            owner,
            workspace => workspace.EditAsync(evaluationId, 1, AddGroupWithMeasurement)
        );

        // Without a share there is nothing to export.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.TransferAsync(invited, transfer => transfer.ExportAsync(evaluationId))
        );

        // Act: as a reader, export the evaluation and import the file again.
        await fixture.ActAsync(
            owner,
            workspace =>
                workspace.ShareAsync(evaluationId, 2, "reader@example.test", AccessLevel.Reader)
        );
        var data = await fixture.TransferAsync(
            invited,
            transfer => transfer.ExportAsync(evaluationId)
        );
        var importedId = await fixture.TransferAsync(
            invited,
            transfer => transfer.ImportAsync(data)
        );

        // Assert: the copy belongs to the importing user alone and has ids of its own.
        var imported = await fixture.AsAsync(invited, workspace => workspace.ReadAsync(importedId));
        Assert.Equal(invited, imported.OwnerId);
        Assert.Empty(imported.Shares);
        Assert.Equal(12.5m, imported.Criteria.Single().Threshold);
        Assert.Equal(imported.Groups.Single().Id, imported.Criteria.Single().GroupId);
        Assert.NotEqual(evaluationId, importedId);
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(owner, workspace => workspace.ReadAsync(importedId))
        );

        // A rejected file leaves nothing behind: the shared original and the copy are all there is.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.TransferAsync(invited, transfer => transfer.ImportAsync("{}"u8.ToArray()))
        );
        Assert.Equal(2, (await fixture.AsAsync(invited, workspace => workspace.ListAsync())).Count);
    }

    private static void AddGroupWithMeasurement(Evaluation evaluation)
    {
        var group = evaluation.AddGroup("Group");

        evaluation.AddCriterion(
            new(
                "Measurement",
                CriterionType.Measurement,
                Priority.Required,
                CriterionScope.Vehicle,
                group,
                12.5m,
                "units"
            )
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Smtp_quota_is_reserved_persistently_before_delivery(int port)
    {
        // Arrange: a mail server that allows a single message per hour and per day.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var options = Options.Create(
            new SmtpOptions
            {
                Host = "localhost",
                Port = 52025,
                StartTls = false,
                From = "test@example.test",
                HourlyLimit = 1,
                DailyLimit = 1,
            }
        );

        Task SendTestMailAsync()
        {
            return fixture.ExecuteAsync(provider =>
                new SmtpDelivery(
                    provider.GetRequiredService<AppDbContext>(),
                    options,
                    fixture.Clock
                ).SendAsync("quota@example.test", "Test", "Test")
            );
        }

        // Act: the first message uses up the quota, the second one comes from a new scope.
        await SendTestMailAsync();
        var error = await Assert.ThrowsAsync<RuleException>(SendTestMailAsync);

        // Assert
        Assert.Equal("MailQuota", error.Message);
    }

    /// <summary>Tells whether the operation went through or was rejected by a business rule.</summary>
    private static async Task<bool> Attempt<T>(Func<Task<T>> operation)
    {
        try
        {
            await operation();
            return true;
        }
        catch (RuleException)
        {
            return false;
        }
    }
}
