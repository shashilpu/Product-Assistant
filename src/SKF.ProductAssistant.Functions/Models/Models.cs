namespace SKF.ProductAssistant.Functions.Models;

public class ExtractionResult
{
    public string? Product { get; set; }
    public string? Attribute { get; set; }
}

public class QueryResponse
{
    public string Product { get; set; } = string.Empty;
    public string Attribute { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}


