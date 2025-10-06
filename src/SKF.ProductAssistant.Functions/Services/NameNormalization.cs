using System.Text;

namespace SKF.ProductAssistant.Functions.Services;

public static class NameNormalization
{
    public static string NormalizeAttribute(string attribute)
    {
        var lowered = attribute.Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-') sb.Append('_');
        }
        var norm = sb.ToString();
        while (norm.Contains("__")) norm = norm.Replace("__", "_");
        norm = norm.Trim('_');

        return norm switch
        {
            // Dimensions
            "b" => "width",
            "width" => "width",
            "h" => "height",
            "height" => "height",
            "d" => "inner_diameter",
            "id" => "inner_diameter",
            "bore" => "inner_diameter",
            "inner" => "inner_diameter",
            "inner_diameter" => "inner_diameter",
            "od" => "outer_diameter",
            "outer" => "outer_diameter",
            "outside" => "outer_diameter",
            "outer_diameter" => "outer_diameter",
            "outside_diameter" => "outer_diameter",
            "diameter" => "diameter",

            // Performance
            "reference_speed" => "reference_speed",
            "limiting_speed" => "limiting_speed",
            "basic_dynamic_load_rating" => "basic_dynamic_load_rating",
            "basic_static_load_rating" => "basic_static_load_rating",
            "dynamic_load_rating" => "basic_dynamic_load_rating",
            "static_load_rating" => "basic_static_load_rating",
            "c" => "basic_dynamic_load_rating",
            "c0" => "basic_static_load_rating",

            // Properties
            "material" => "material_bearing",
            "material_bearing" => "material_bearing",
            "cage" => "cage",
            "bore_type" => "bore_type",
            "coating" => "coating",
            "number_of_rows" => "number_of_rows",
            "rows" => "number_of_rows",
            "lubricant" => "lubricant",
            "relubrication_feature" => "relubrication_feature",
            "locating_feature_bearing_outer_ring" => "locating_feature_bearing_outer_ring",
            "tolerance_class" => "tolerance_class",
            "filling_slots" => "filling_slots",
            "sealing" => "sealing",
            "radial_internal_clearance" => "radial_internal_clearance",
            "matched_arrangement" => "matched_arrangement",

            // Logistics
            "ean" => "ean_code",
            "ean_code" => "ean_code",
            "products_per_pallet" => "products_per_pallet",
            "pack_code" => "pack_code",
            "pack_gross_weight" => "pack_gross_weight",
            "pack_height" => "pack_height",
            "pack_length" => "pack_length",
            "pack_volume" => "pack_volume",
            "pack_width" => "pack_width",
            "products_per_pack" => "products_per_pack",
            "collecting_pack_quantity" => "collecting_pack_quantity",
            "eclass_code" => "eclass_code",
            "product_net_weight" => "product_net_weight",
            "unspsc_code" => "unspsc_code",

            _ => norm
        };
    }

    public static string NormalizeNameToKey(string name)
    {
        var lowered = name.Trim().ToLowerInvariant();
        if (lowered.Contains("reference speed")) return "reference_speed";
        if (lowered.Contains("limiting speed")) return "limiting_speed";
        if (lowered.Contains("basic dynamic load rating")) return "basic_dynamic_load_rating";
        if (lowered.Contains("basic static load rating")) return "basic_static_load_rating";
        if (lowered.Contains("eclass")) return "eclass_code";
        if (lowered.Contains("ean")) return "ean_code";
        if (lowered.Contains("unspsc")) return "unspsc_code";
        if (lowered.Contains("material")) return "material_bearing";

        var sb = new StringBuilder();
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch)) sb.Append('_');
        }
        var key = sb.ToString();
        while (key.Contains("__")) key = key.Replace("__", "_");
        return key.Trim('_');
    }
}



