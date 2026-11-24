using Steervo.Core;

namespace Steervo.Web.Pages.Shared;

/// <summary>One assessed criterion in the coloured summary of a car or an offer.</summary>
public sealed record FulfillmentEntry(
    Criterion Criterion,
    Fulfillment Result,
    string? Value = null
);

/// <summary>Counts the assessment states of one car or offer; it states facts and derives no grade.</summary>
public sealed record FulfillmentSummary(string Key, IReadOnlyList<FulfillmentEntry> Entries)
{
    // The order in which the states appear in the bar and in the list of counts.
    private static readonly Fulfillment[] Order =
    [
        Fulfillment.Met,
        Fulfillment.Unmet,
        Fulfillment.Optional,
        Fulfillment.Unknown,
        Fulfillment.Rated,
    ];

    /// <summary>The entries grouped by state in display order; states without entries are left out.</summary>
    public IReadOnlyList<(Fulfillment State, IReadOnlyList<FulfillmentEntry> Entries)> Groups
    {
        get
        {
            var groups = new List<(Fulfillment State, IReadOnlyList<FulfillmentEntry> Entries)>();

            foreach (var state in Order)
            {
                var entriesInState = Entries.Where(entry => entry.Result == state).ToList();

                if (entriesInState.Count > 0)
                {
                    groups.Add((state, entriesInState));
                }
            }

            return groups;
        }
    }
}
