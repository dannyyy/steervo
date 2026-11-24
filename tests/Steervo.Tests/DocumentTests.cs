using System.Security.Cryptography;
using System.Text;
using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class DocumentTests
{
    /// <summary>Describes a stored file whose length and digest match the given text.</summary>
    internal static DocumentFile File(string name = "offer.pdf", string value = "first")
    {
        var length = Encoding.UTF8.GetByteCount(value);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        return new(Guid.NewGuid(), name, length, sha256);
    }

    [Fact]
    public void Replacement_preserves_bytes_metadata_and_offer_reference_and_copy_excludes_documents()
    {
        // Arrange: a document attached to the first version of an offer.
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Vehicle"));
        var offer = evaluation.AddOffer(candidate, new() { Name = "First" }, [], "EUR");
        var first = File();
        var documentId = evaluation.AddDocument(offer, "Offer PDF", first, "Original");

        // Act: the offer gets a second version, then the document is replaced.
        evaluation.ReviseOffer(offer, new() { Name = "Second" }, []);
        evaluation.ReviseDocument(documentId, File(value: "second"), "Replaced");

        // Assert: each document version remembers the offer version it was added for.
        var document = evaluation.FindDocument(documentId);
        Assert.Equal(first, document.Revisions[0].File);
        Assert.Equal(1, document.Revisions[0].OfferRevisionNumber);
        Assert.Equal(2, document.Latest.Number);
        Assert.Equal(2, document.Latest.OfferRevisionNumber);

        // Copying the car does not copy its documents.
        evaluation.CopyCandidate(candidate, "Copy");
        Assert.Single(evaluation.Documents);

        // Documents go when their offer or their car goes.
        var other = evaluation.AddOffer(candidate, new() { Name = "Other" }, []);
        evaluation.AddDocument(other, "Other PDF", File(value: "other"));
        evaluation.RemoveOffer(other);
        Assert.Single(evaluation.Documents);

        evaluation.RemoveCandidate(candidate);
        Assert.Empty(evaluation.Documents);
    }

    [
        Theory,
        InlineData("../secret"),
        InlineData("..\\secret"),
        InlineData("."),
        InlineData("file\r\nheader"),
        InlineData("")
    ]
    public void File_names_cannot_control_storage_paths_or_response_headers(string name)
    {
        Assert.Throws<RuleException>(() => File(name).Validated());
    }

    [Fact]
    public void File_limits_and_history_cannot_be_bypassed()
    {
        // A single file must not be empty, too large or carry a malformed digest.
        Assert.Throws<RuleException>(() => (File() with { Length = 0 }).Validated());
        Assert.Throws<RuleException>(() =>
            (File() with { Length = DocumentFile.MaximumBytes + 1 }).Validated()
        );
        Assert.Throws<RuleException>(() => (File() with { Sha256 = "bad" }).Validated());

        // Eight files of the maximum size use up what one evaluation may store.
        const int filesUntilFull = 8;
        var evaluation = new Evaluation("owner", "Decision", null);
        var offer = evaluation.AddOffer(
            evaluation.AddCandidate(new("Vehicle")),
            new() { Name = "First" },
            [],
            "EUR"
        );
        for (var i = 0; i < filesUntilFull; i++)
        {
            evaluation.AddDocument(
                offer,
                "File",
                File() with
                {
                    Length = DocumentFile.MaximumBytes,
                }
            );
        }

        Assert.Throws<RuleException>(() => evaluation.AddDocument(offer, "Overflow", File()));
        Assert.Equal(filesUntilFull, evaluation.Documents.Count);
    }

    [Fact]
    public void A_file_with_an_absurd_length_is_rejected_as_an_invalid_document()
    {
        // Arrange: an offer that already has one document.
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Vehicle"));
        var offer = evaluation.AddOffer(candidate, new() { Name = "First" }, [], "EUR");
        var documentId = evaluation.AddDocument(offer, "Offer PDF", File());
        var absurd = File(value: "second") with { Length = long.MaxValue };
        var oversized = File(value: "third") with { Length = Evaluation.MaximumDocumentBytes };

        // Act: the length is checked before it is added to what the evaluation already stores.
        var replaced = Assert.Throws<RuleException>(() =>
            evaluation.ReviseDocument(documentId, absurd)
        );
        var replacedOversized = Assert.Throws<RuleException>(() =>
            evaluation.ReviseDocument(documentId, oversized)
        );
        var added = Assert.Throws<RuleException>(() =>
            evaluation.AddDocument(offer, "Other PDF", absurd)
        );

        // Assert
        Assert.Equal("InvalidDocument", replaced.Message);
        Assert.Equal("InvalidDocument", replacedOversized.Message);
        Assert.Equal("InvalidDocument", added.Message);
        Assert.Single(evaluation.Documents);
        Assert.Single(evaluation.FindDocument(documentId).Revisions);

        // A valid replacement is still accepted afterwards.
        evaluation.ReviseDocument(documentId, File(value: "second"));

        Assert.Equal(2, evaluation.FindDocument(documentId).Latest.Number);
    }
}
