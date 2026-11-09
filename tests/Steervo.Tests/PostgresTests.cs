using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Steervo.Application.Accounts;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Mail;
using Xunit;

namespace Steervo.Tests;

[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection;

[Collection("Postgres")]
public sealed class PostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migrations_start_empty_and_setup_is_single_use_under_competing_requests(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        Assert.False(await fixture.AccountAsync(a => a.IsInitializedAsync()));
        Assert.Equal(0, await fixture.RunAsync(p => p.GetRequiredService<AppDbContext>().Evaluations.CountAsync()));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.BeginSetupAsync("bad", "a@example.test", "A")));
        var first = await fixture.AccountAsync(a => a.BeginSetupAsync(PostgresFixture.SetupToken, "a@example.test", "A"));
        var second = await fixture.AccountAsync(a => a.BeginSetupAsync(PostgresFixture.SetupToken, "b@example.test", "B"));
        var firstCode = fixture.Mail.Codes["A@EXAMPLE.TEST"]; var secondCode = fixture.Mail.Codes["B@EXAMPLE.TEST"];
        var results = await Task.WhenAll(Attempt(() => fixture.AccountAsync(a => a.VerifyCodeAsync(first, firstCode))), Attempt(() => fixture.AccountAsync(a => a.VerifyCodeAsync(second, secondCode))));
        Assert.Single(results, x => x);
        Assert.Equal(1, await fixture.RunAsync(p => p.GetRequiredService<AppDbContext>().Users.CountAsync()));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.BeginSetupAsync(PostgresFixture.SetupToken, "c@example.test", "C")));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Codes_expire_are_consumed_once_and_have_bounded_attempts(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        var id = await fixture.AccountAsync(a => a.RequestCodeAsync("owner@example.test"));
        var code = fixture.Mail.Codes["OWNER@EXAMPLE.TEST"];
        Assert.Equal(owner, await fixture.AccountAsync(a => a.VerifyCodeAsync(id, code)));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.VerifyCodeAsync(id, code)));
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        id = await fixture.AccountAsync(a => a.RequestCodeAsync("owner@example.test")); code = fixture.Mail.Codes["OWNER@EXAMPLE.TEST"];
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.VerifyCodeAsync(id, wrong)));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.VerifyCodeAsync(id, code)));
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        id = await fixture.AccountAsync(a => a.RequestCodeAsync("owner@example.test")); code = fixture.Mail.Codes["OWNER@EXAMPLE.TEST"];
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.VerifyCodeAsync(id, code)));
        var unknown = await fixture.AccountAsync(a => a.RequestCodeAsync("unknown@example.test"));
        Assert.False(fixture.Mail.Codes.ContainsKey("UNKNOWN@EXAMPLE.TEST"));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.VerifyCodeAsync(unknown, "000000")));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Invitations_are_primary_only_expire_and_do_not_share_records(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync(); var invited = await fixture.InviteAsync(owner, "invited@example.test");
        var caseId = await fixture.AsAsync(owner, w => w.CreateAsync("Private decision", null));
        Assert.Empty(await fixture.AsAsync(invited, w => w.ListAsync()));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(invited, w => w.ReadAsync(caseId)));
        await Assert.ThrowsAsync<RuleException>(() => fixture.RunAsync(async p => { await p.GetRequiredService<IAccountAccess>().InviteAsync(invited, "third@example.test", "Third"); return true; }));
        await fixture.RunAsync(async p => { await p.GetRequiredService<IAccountAccess>().InviteAsync(owner, "expired@example.test", "Expired"); return true; });
        var codeId = await fixture.AccountAsync(a => a.RequestCodeAsync("expired@example.test"));
        var invitationId = (await fixture.AccountAsync(a => a.InvitationsAsync(owner))).Single(x => x.Email == "EXPIRED@EXAMPLE.TEST").Id;
        await fixture.RunAsync(async p => { await p.GetRequiredService<IAccountAccess>().RevokeInvitationAsync(owner, invitationId); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AccountAsync(a => a.VerifyCodeAsync(codeId, fixture.Mail.Codes["EXPIRED@EXAMPLE.TEST"])));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Resource_permissions_and_optimistic_concurrency_hold_across_database_connections(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync(); var editor = await fixture.InviteAsync(owner, "editor@example.test"); var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var id = await fixture.AsAsync(editor, w => w.CreateAsync("Owned by invited user", null));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(owner, w => w.ReadAsync(id)));
        await fixture.AsAsync(editor, async w => { await w.ShareAsync(id, 1, "reader@example.test", AccessLevel.Reader); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(reader, async w => { await w.EditAsync(id, 2, e => e.AddGroup("Blocked")); return true; }));
        await fixture.AsAsync(editor, async w => { await w.ShareAsync(id, 2, "reader@example.test", AccessLevel.Editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(reader, async w => { await w.ShareAsync(id, 3, "owner@example.test", AccessLevel.Reader); return true; }));
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(reader, async w => { await w.DeleteAsync(id, 3); return true; }));
        var results = await Task.WhenAll(Attempt(() => fixture.AsAsync(editor, async w => { await w.EditAsync(id, 3, e => e.AddGroup("First writer")); return true; })), Attempt(() => fixture.AsAsync(reader, async w => { await w.EditAsync(id, 3, e => e.AddGroup("Second writer")); return true; })));
        Assert.Single(results, x => x);
        var evaluation = await fixture.AsAsync(editor, w => w.ReadAsync(id));
        Assert.Equal(4, evaluation.Version); Assert.Single(evaluation.Groups);
        await fixture.AsAsync(editor, async w => { await w.EditAsync(id, 4, e => e.AddCriterion(new("Criterion", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle, e.Groups.Single().Id))); return true; });
        var criterionId = (await fixture.AsAsync(reader, w => w.ReadAsync(id))).Criteria.Single().Id;
        await fixture.AsAsync(reader, async w => { await w.EditAsync(id, 5, e => e.RemoveGroup(e.Groups.Single().Id)); return true; });
        evaluation = await fixture.AsAsync(reader, w => w.ReadAsync(id));
        Assert.Equal(criterionId, evaluation.Criteria.Single().Id); Assert.Null(evaluation.Criteria.Single().GroupId);
        await fixture.AsAsync(editor, async w => { await w.RevokeAsync(id, 6, reader); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(reader, w => w.ReadAsync(id)));
        await fixture.AsAsync(editor, async w => { await w.EditAsync(id, 7, e => e.Reassign(criterionId, e.AddGroup("Final group"))); return true; });
        await fixture.AsAsync(editor, async w => { await w.DeleteAsync(id, 8); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(editor, w => w.ReadAsync(id)));
        Assert.Equal(0, await fixture.RunAsync(p => p.GetRequiredService<AppDbContext>().Set<Criterion>().CountAsync()));
        Assert.Equal(0, await fixture.RunAsync(p => p.GetRequiredService<AppDbContext>().Set<CriterionGroup>().CountAsync()));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Export_requires_access_and_import_creates_a_private_independent_case_atomically(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync(); var invited = await fixture.InviteAsync(owner, "reader@example.test");
        var id = await fixture.AsAsync(owner, w => w.CreateAsync("Export test", "User-entered purpose"));
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => { var group = e.AddGroup("Group"); e.AddCriterion(new("Measurement", CriterionType.Measurement, Priority.Required, CriterionScope.Vehicle, group, 12.5m, "units")); }); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(invited, t => t.ExportAsync(id)));
        await fixture.AsAsync(owner, async w => { await w.ShareAsync(id, 2, "reader@example.test", AccessLevel.Reader); return true; });
        var data = await fixture.TransferAsync(invited, t => t.ExportAsync(id));
        var importedId = await fixture.TransferAsync(invited, t => t.ImportAsync(data));
        var imported = await fixture.AsAsync(invited, w => w.ReadAsync(importedId));
        Assert.Equal(invited, imported.OwnerId); Assert.Empty(imported.Shares);
        Assert.Equal(12.5m, imported.Criteria.Single().Threshold);
        Assert.Equal(imported.Groups.Single().Id, imported.Criteria.Single().GroupId);
        Assert.NotEqual(id, importedId);
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(owner, w => w.ReadAsync(importedId)));
        await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(invited, t => t.ImportAsync("{}"u8.ToArray())));
        Assert.Equal(2, (await fixture.AsAsync(invited, w => w.ListAsync())).Count);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Smtp_quota_is_reserved_persistently_before_delivery(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var options = Options.Create(new SmtpOptions { Host = "localhost", Port = 52025, StartTls = false, From = "test@example.test", HourlyLimit = 1, DailyLimit = 1 });
        await fixture.RunAsync(async p => { await new SmtpDelivery(p.GetRequiredService<AppDbContext>(), options, fixture.Clock).SendAsync("quota@example.test", "Test", "Test"); return true; });
        var error = await Assert.ThrowsAsync<RuleException>(() => fixture.RunAsync(async p => { await new SmtpDelivery(p.GetRequiredService<AppDbContext>(), options, fixture.Clock).SendAsync("quota@example.test", "Test", "Test"); return true; }));
        Assert.Equal("MailQuota", error.Message);
    }
    private static async Task<bool> Attempt<T>(Func<Task<T>> operation)
    {
        try { await operation(); return true; } catch (RuleException) { return false; }
    }
}
