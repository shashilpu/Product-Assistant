using System.Text.Json;
using Microsoft.Extensions.Logging;
using SKF.ProductAssistant.Functions.Services;

namespace SKF.ProductAssistant.Functions.Storage;

public interface IDataSheetRepository
{
    bool TryGetAttribute(string product, string attribute, out string? value);
}

public class JsonDataSheetRepository : IDataSheetRepository
{
    private readonly string _dataDirectory;
    private readonly ILogger<JsonDataSheetRepository> _logger;

    private readonly Dictionary<string, Dictionary<string, string>> _productCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public JsonDataSheetRepository(string dataDirectory, ILogger<JsonDataSheetRepository> logger)
    {
        _dataDirectory = dataDirectory;
        _logger = logger;
    }

    public bool TryGetAttribute(string product, string attribute, out string? value)
    {
        EnsureLoaded();
        value = null;

        if (_productCache.TryGetValue(product, out var attrs))
        {
            // normalize attribute keys: try direct, then common synonyms
            if (attrs.TryGetValue(attribute, out value)) return true;

            var normalized = NameNormalization.NormalizeAttribute(attribute);
            if (attrs.TryGetValue(normalized, out value)) return true;

            // try some common alternates
            foreach (var key in attrs.Keys)
            {
                if (string.Equals(NameNormalization.NormalizeAttribute(key), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    value = attrs[key];
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeAttribute(string attribute) => NameNormalization.NormalizeAttribute(attribute);

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!Directory.Exists(_dataDirectory))
        {
            _logger.LogWarning("Datasheet directory not found: {Dir}", _dataDirectory);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_dataDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));

                // Support two possible shapes:
                // 1) Single product object with attributes
                // 2) Array of products: [{ product: "6205", width: "15mm", ... }]
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    LoadProductObject(doc.RootElement);
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            LoadProductObject(element);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load datasheet file {File}", file);
            }
        }
    }

    private void LoadProductObject(JsonElement obj)
    {
        string? product = null;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in obj.EnumerateObject())
        {
            var name = prop.Name;

            // Determine product identifier from common fields
            if (string.Equals(name, "product", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "designation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "number", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "sku", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "title", StringComparison.OrdinalIgnoreCase))
            {
                var idValue = prop.Value.ToString();
                if (!string.IsNullOrWhiteSpace(idValue))
                {
                    product = idValue.Trim();
                }
                continue;
            }

            // Flatten well-known nested structures (e.g., dimensions arrays from PIM)
            if (string.Equals(name, "dimensions", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var dim in prop.Value.EnumerateArray())
                {
                    if (dim.ValueKind != JsonValueKind.Object) continue;

                    string dimName = dim.TryGetProperty("name", out var nEl) ? nEl.ToString() : string.Empty;
                    string unit = dim.TryGetProperty("unit", out var uEl) ? uEl.ToString() : string.Empty;
                    string symbol = dim.TryGetProperty("symbol", out var sEl) ? sEl.ToString() : string.Empty;

                    // numeric value preferred; fall back to string
                    string dimValue;
                    if (dim.TryGetProperty("value", out var vEl))
                    {
                        dimValue = vEl.ValueKind switch
                        {
                            JsonValueKind.Number => vEl.TryGetDouble(out var d) ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : vEl.ToString(),
                            _ => vEl.ToString()
                        };
                    }
                    else
                    {
                        continue;
                    }

                    // Compose display like "15mm" when unit exists
                    var display = string.IsNullOrWhiteSpace(unit) ? dimValue : $"{dimValue}{unit}";

                    // Map to normalized attribute keys by symbol or name
                    string? key = null;
                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        switch (symbol.Trim())
                        {
                            case "B": key = "width"; break;
                            case "d": key = "inner_diameter"; break;
                            case "D": key = "outer_diameter"; break;
                        }
                    }
                    if (key == null && !string.IsNullOrWhiteSpace(dimName))
                    {
                        var dn = dimName.Trim().ToLowerInvariant();
                        if (dn.Contains("width")) key = "width";
                        else if (dn.Contains("bore")) key = "inner_diameter";
                        else if (dn.Contains("inner diameter")) key = "inner_diameter";
                        else if (dn.Contains("outside diameter") || dn.Contains("outer diameter")) key = "outer_diameter";
                        else if (dn == "diameter") key = "diameter";
                    }

                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        attributes[key] = display;
                    }
                }
                continue;
            }

            // Flatten performance metrics (name/value[/unit]) into snake_case keys
            if (string.Equals(name, "performance", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var perf in prop.Value.EnumerateArray())
                {
                    if (perf.ValueKind != JsonValueKind.Object) continue;

                    string perfName = perf.TryGetProperty("name", out var nEl) ? nEl.ToString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(perfName)) continue;

                    string unit = perf.TryGetProperty("unit", out var uEl) ? uEl.ToString() : string.Empty;
                    string perfValue;
                    if (perf.TryGetProperty("value", out var vEl))
                    {
                        perfValue = vEl.ValueKind switch
                        {
                            JsonValueKind.Number => vEl.TryGetDouble(out var d) ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : vEl.ToString(),
                            _ => vEl.ToString()
                        };
                    }
                    else continue;

                    var display = string.IsNullOrWhiteSpace(unit) ? perfValue : $"{perfValue} {unit}";

                    // Normalize the performance name to a snake_case key
                    var key = NormalizeNameToKey(perfName);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        attributes[key] = display;
                    }
                }
                continue;
            }

            // Flatten properties array
            if (string.Equals(name, "properties", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in prop.Value.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    string entryName = entry.TryGetProperty("name", out var nEl) ? nEl.ToString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(entryName)) continue;
                    string value = entry.TryGetProperty("value", out var vEl) ? vEl.ToString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var key = NormalizeNameToKey(entryName);
                    if (!string.IsNullOrWhiteSpace(key)) attributes[key] = value;
                }
                continue;
            }

            // Flatten logistics array
            if (string.Equals(name, "logistics", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in prop.Value.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    string entryName = entry.TryGetProperty("name", out var nEl) ? nEl.ToString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(entryName)) continue;
                    string unit = entry.TryGetProperty("unit", out var uEl) ? uEl.ToString() : string.Empty;
                    string value = entry.TryGetProperty("value", out var vEl) ? vEl.ToString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var display = string.IsNullOrWhiteSpace(unit) ? value : $"{value} {unit}";
                    var key = NormalizeNameToKey(entryName);
                    if (!string.IsNullOrWhiteSpace(key)) attributes[key] = display;
                }
                continue;
            }

            // Flatten specifications array
            if (string.Equals(name, "specifications", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in prop.Value.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    string entryName = entry.TryGetProperty("name", out var nEl) ? nEl.ToString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(entryName)) continue;
                    string value = entry.TryGetProperty("value", out var vEl) ? vEl.ToString() : string.Empty;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var key = NormalizeNameToKey(entryName);
                    if (!string.IsNullOrWhiteSpace(key)) attributes[key] = value;
                }
                continue;
            }

            // Default: store raw string for any other simple fields
            attributes[name] = prop.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(product))
        {
            _productCache[product!] = attributes;
        }
    }

    private static string NormalizeNameToKey(string name) => NameNormalization.NormalizeNameToKey(name);
}


