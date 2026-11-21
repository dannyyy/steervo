using Steervo.Core;

namespace Steervo.Web.Pages.Shared;

/// <summary>One assessed criterion in the coloured summary of a car or an offer.</summary>
public sealed record FulfillmentEntry(Criterion Criterion, Fulfillment Result, string? Value = null);

/// <summary>Counts the assessment states of one car or offer; it states facts and derives no grade.</summary>
public sealed record FulfillmentSummary(string Key, IReadOnlyList<FulfillmentEntry> Entries)
{
    private static readonly Fulfillment[] Order = [Fulfillment.Met, Fulfillment.Unmet, Fulfillment.Optional, Fulfillment.Unknown, Fulfillment.Rated];
    public IReadOnlyList<(Fulfillment State, IReadOnlyList<FulfillmentEntry> Entries)> Groups =>
        Order.Select(state => (state, (IReadOnlyList<FulfillmentEntry>)Entries.Where(entry => entry.Result == state).ToList())).Where(group => group.Item2.Count > 0).ToList();
}
