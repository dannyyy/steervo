using System.Text.Json.Nodes;
using Steervo.Core;

namespace Steervo.Application.Evaluations;

/// <summary>
/// Rewrites the JSON of files written by older versions so that the current, strict reader
/// accepts them.
/// </summary>
internal static class LegacyExportUpgrade
{
    // Files written before this version carried one base price and an archive flag per offer.
    private const int OfferLinesSinceVersion = 9;
    private const int MaximumLineNameLength = 300;

    /// <summary>
    /// Turns the base price of every offer version into its vehicle line and drops the archive
    /// flag; archived offers are imported like any other. Newer files are returned untouched.
    /// </summary>
    public static JsonNode? Apply(JsonNode? root)
    {
        if (root is not JsonObject content || !IsWrittenBeforeOfferLines(content))
        {
            return root;
        }

        if (content["offers"] is not JsonArray offers)
        {
            return root;
        }

        foreach (var offer in offers.OfType<JsonObject>())
        {
            offer.Remove("archived");

            if (offer["revisions"] is not JsonArray revisions)
            {
                continue;
            }

            foreach (var revision in revisions.OfType<JsonObject>())
            {
                MoveBasePriceToVehicleLine(revision);
            }
        }

        return root;
    }

    private static bool IsWrittenBeforeOfferLines(JsonObject content)
    {
        return content["version"] is JsonValue number
            && number.TryGetValue<int>(out var version)
            && version < OfferLinesSinceVersion;
    }

    private static void MoveBasePriceToVehicleLine(JsonObject revision)
    {
        if (revision["terms"] is not JsonObject terms)
        {
            return;
        }

        // The legacy property is removed even when no line can be created from it.
        if (!terms.Remove("basePrice", out var price) || price is null)
        {
            return;
        }

        if (revision["lines"] is not JsonArray lines)
        {
            return;
        }

        var name = VehicleLineName(terms);
        var vehicleLine = new JsonObject
        {
            ["name"] = name[..Math.Min(name.Length, MaximumLineNameLength)],
            ["kind"] = nameof(OfferLineKind.Vehicle),
            ["priceKind"] = nameof(OfferPriceKind.Amount),
            ["amount"] = price.DeepClone(),
        };

        lines.Insert(0, vehicleLine);
    }

    private static string VehicleLineName(JsonObject terms)
    {
        if (
            terms["vehicleDescription"] is JsonValue text
            && text.TryGetValue<string>(out var description)
            && !string.IsNullOrWhiteSpace(description)
        )
        {
            return description.Trim();
        }

        return EvaluationTransfer.LegacyVehicleLine;
    }
}
