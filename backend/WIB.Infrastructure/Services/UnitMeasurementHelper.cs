using System.Text.RegularExpressions;

namespace WIB.Infrastructure.Services;

public static class UnitMeasurementHelper
{
    // Regex patterns for different unit types
    private static readonly Dictionary<UnitType, List<Regex>> UnitPatterns = new()
    {
        {
            UnitType.Weight, new List<Regex>
            {
                new(@"\b(\d+(?:[.,]\d+)?)\s*kg\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*g\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*grammi?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*chilogrammi?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*gr\.?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\bkg\s*(\d+(?:[.,]\d+)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            }
        },
        {
            UnitType.Volume, new List<Regex>
            {
                new(@"\b(\d+(?:[.,]\d+)?)\s*[lL]\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*litri?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*ml\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*millilitri?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*cl\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*centilitri?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            }
        },
        {
            UnitType.Length, new List<Regex>
            {
                new(@"\b(\d+(?:[.,]\d+)?)\s*m\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*metri?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*cm\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*centimetri?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*mm\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*millimetri?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            }
        },
        {
            UnitType.Quantity, new List<Regex>
            {
                new(@"\b(\d+)\s*pz\.?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+)\s*pezzi?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+)\s*pcs?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+)\s*x\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\bx\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+)\s*confezioni?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+)\s*pack\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            }
        },
        {
            UnitType.Area, new List<Regex>
            {
                new(@"\b(\d+(?:[.,]\d+)?)\s*m2\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*mq\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"\b(\d+(?:[.,]\d+)?)\s*metri\s*quadri?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            }
        }
    };

    // Weight-based pricing patterns (prezzo al kg/etto)
    private static readonly List<Regex> WeightPricingPatterns = new()
    {
        new(@"€/kg", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"al\s*kg", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"per\s*kg", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"€\s*(\d+(?:[.,]\d+)?)/kg", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"prezzo\s*al\s*chilo", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"€/etto", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"al\s*etto", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    public enum UnitType
    {
        Weight,    // kg, g, grammi
        Volume,    // l, ml, cl, litri
        Length,    // m, cm, mm, metri
        Area,      // m2, mq, metri quadri
        Quantity,  // pz, pezzi, x, confezioni
        None
    }

    public class UnitMeasurement
    {
        public UnitType Type { get; set; }
        public decimal Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string OriginalMatch { get; set; } = string.Empty;
    }

    /// <summary>
    /// Extracts unit measurements from product label
    /// </summary>
    public static List<UnitMeasurement> ExtractUnits(string label)
    {
        var results = new List<UnitMeasurement>();
        if (string.IsNullOrEmpty(label)) return results;

        foreach (var (unitType, patterns) in UnitPatterns)
        {
            foreach (var pattern in patterns)
            {
                var matches = pattern.Matches(label);
                foreach (Match match in matches)
                {
                    if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), out decimal value))
                    {
                        results.Add(new UnitMeasurement
                        {
                            Type = unitType,
                            Value = value,
                            Unit = ExtractUnitFromMatch(match.Value),
                            OriginalMatch = match.Value
                        });
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Checks if the product has weight-based pricing (prezzo al kg/etto)
    /// </summary>
    public static bool HasWeightBasedPricing(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;

        return WeightPricingPatterns.Any(pattern => pattern.IsMatch(label));
    }

    /// <summary>
    /// Normalizes weight to kilograms for price comparison
    /// </summary>
    public static decimal? NormalizeWeightToKg(UnitMeasurement measurement)
    {
        if (measurement.Type != UnitType.Weight) return null;

        var unit = measurement.Unit.ToLowerInvariant();
        return unit switch
        {
            "kg" or "chilogrammi" or "chilogrammo" => measurement.Value,
            "g" or "gr" or "grammi" or "grammo" => measurement.Value / 1000m,
            _ => measurement.Value // Assume kg as default
        };
    }

    /// <summary>
    /// Normalizes volume to liters for price comparison
    /// </summary>
    public static decimal? NormalizeVolumeToLiters(UnitMeasurement measurement)
    {
        if (measurement.Type != UnitType.Volume) return null;

        var unit = measurement.Unit.ToLowerInvariant();
        return unit switch
        {
            "l" or "litri" or "litro" => measurement.Value,
            "ml" or "millilitri" or "millilitro" => measurement.Value / 1000m,
            "cl" or "centilitri" or "centilitro" => measurement.Value / 100m,
            _ => measurement.Value // Assume liters as default
        };
    }

    /// <summary>
    /// Calculates price per unit (kg, liter, etc.) when possible
    /// </summary>
    public static decimal? CalculatePricePerUnit(string label, decimal unitPrice, decimal quantity = 1)
    {
        var units = ExtractUnits(label);
        if (units.Count == 0) return null;

        // Find weight or volume measurements
        var weightUnit = units.FirstOrDefault(u => u.Type == UnitType.Weight);
        if (weightUnit != null)
        {
            var normalizedWeight = NormalizeWeightToKg(weightUnit);
            if (normalizedWeight.HasValue && normalizedWeight.Value > 0)
            {
                return unitPrice / (normalizedWeight.Value * quantity);
            }
        }

        var volumeUnit = units.FirstOrDefault(u => u.Type == UnitType.Volume);
        if (volumeUnit != null)
        {
            var normalizedVolume = NormalizeVolumeToLiters(volumeUnit);
            if (normalizedVolume.HasValue && normalizedVolume.Value > 0)
            {
                return unitPrice / (normalizedVolume.Value * quantity);
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the unit part from a regex match
    /// </summary>
    private static string ExtractUnitFromMatch(string match)
    {
        // Remove digits, spaces, and common separators to get the unit
        return Regex.Replace(match, @"[\d\s.,]+", "").Trim();
    }

    /// <summary>
    /// Gets a user-friendly description of the unit type
    /// </summary>
    public static string GetUnitTypeDescription(UnitType unitType)
    {
        return unitType switch
        {
            UnitType.Weight => "Peso",
            UnitType.Volume => "Volume",
            UnitType.Length => "Lunghezza",
            UnitType.Area => "Area",
            UnitType.Quantity => "Quantità",
            _ => "Sconosciuto"
        };
    }

    /// <summary>
    /// Determines if a product is typically sold by weight
    /// </summary>
    public static bool IsTypicallyWeightBased(string label)
    {
        var weightIndicators = new[]
        {
            "frutta", "verdura", "carne", "pesce", "salumi", "formaggi",
            "pasta", "riso", "farina", "zucchero", "caffè", "pane",
            "fresco", "sfuso", "banco", "macelleria", "pescheria",
            "salumeria", "gastronomia", "kg", "etto", "grammi"
        };

        var normalizedLabel = label.ToLowerInvariant();
        return weightIndicators.Any(indicator => normalizedLabel.Contains(indicator));
    }
}