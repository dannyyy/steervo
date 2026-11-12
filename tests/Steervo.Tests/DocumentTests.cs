using System.Security.Cryptography;
using System.Text;
using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class DocumentTests
{
    internal static DocumentFile File(string name = "offer.pdf", string value = "first") => new(Guid.NewGuid(), name, Encoding.UTF8.GetByteCount(value), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))));
    [Fact]
    public void Replacement_preserves_bytes_metadata_and_offer_reference_and_copy_excludes_documents()
    {
        var evaluation = new Evaluation("owner", "Decision", null); var candidate = evaluation.AddCandidate(new("Vehicle"));
        var offer = evaluation.AddOffer(candidate, new() { Name = "First" }, [], "EUR");
        var first = File(); var id = evaluation.AddDocument(offer, "Offer PDF", first, "Original");
        evaluation.ReviseOffer(offer, new() { Name = "Second" }, []);
        evaluation.ReviseDocument(id, File(value: "second"), "Replaced");
        var document = evaluation.FindDocument(id);
        Assert.Equal(first, document.Revisions[0].File); Assert.Equal(1, document.Revisions[0].OfferRevisionNumber);
        Assert.Equal(2, document.Latest.Number); Assert.Equal(2, document.Latest.OfferRevisionNumber);
        evaluation.CopyCandidate(candidate, "Copy"); Assert.Single(evaluation.Documents);
        evaluation.ArchiveOffer(offer, true);
        Assert.Throws<RuleException>(() => evaluation.ReviseDocument(id, File()));
        Assert.Throws<RuleException>(() => evaluation.RemoveDocument(id));
        evaluation.RemoveCandidate(candidate); Assert.Empty(evaluation.Documents);
    }
    [Theory, InlineData("../secret"), InlineData("..\\secret"), InlineData("."), InlineData("file\r\nheader"), InlineData("")]
    public void File_names_cannot_control_storage_paths_or_response_headers(string name) => Assert.Throws<RuleException>(() => File(name).Validated());
    [Fact]
    public void File_limits_and_history_cannot_be_bypassed()
    {
        Assert.Throws<RuleException>(() => (File() with { Length = 0 }).Validated());
        Assert.Throws<RuleException>(() => (File() with { Length = DocumentFile.MaximumBytes + 1 }).Validated());
        Assert.Throws<RuleException>(() => (File() with { Sha256 = "bad" }).Validated());
        var evaluation = new Evaluation("owner", "Decision", null); var offer = evaluation.AddOffer(evaluation.AddCandidate(new("Vehicle")), new() { Name = "First" }, [], "EUR");
        for (var i = 0; i < 8; i++) evaluation.AddDocument(offer, "File", File() with { Length = DocumentFile.MaximumBytes });
        Assert.Throws<RuleException>(() => evaluation.AddDocument(offer, "Overflow", File()));
        Assert.Equal(8, evaluation.Documents.Count);
    }
}
