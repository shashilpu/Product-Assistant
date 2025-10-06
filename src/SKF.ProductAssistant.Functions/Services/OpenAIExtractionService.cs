using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SKF.ProductAssistant.Functions.Models;

namespace SKF.ProductAssistant.Functions.Services;

public interface IOpenAIExtractionService
{
    Task<ExtractionResult?> ExtractProductAndAttributeAsync(string query);
}

public class OpenAIExtractionService : IOpenAIExtractionService
{
    private readonly ILogger<OpenAIExtractionService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _deployment;
    private readonly string _apiVersion;

    public OpenAIExtractionService(ILogger<OpenAIExtractionService> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _endpoint = config["AZURE_OPENAI_ENDPOINT"] ?? string.Empty;
        _apiKey = config["AZURE_OPENAI_KEY"] ?? string.Empty;
        _deployment = config["AZURE_OPENAI_DEPLOYMENT"] ?? "gpt-4o-mini";
        _apiVersion = config["AZURE_OPENAI_API_VERSION"] ?? "2024-08-01-preview";
    }

    public async Task<ExtractionResult?> ExtractProductAndAttributeAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(_endpoint) || string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Azure OpenAI endpoint or key not configured.");
            return null;
        }

        var url = $"{_endpoint.TrimEnd('/')}/openai/deployments/{_deployment}/chat/completions?api-version={_apiVersion}";

        var systemPrompt = "You extract product designations and attributes from user queries about SKF bearings. Return strict JSON with keys product and attribute. If not detected, use empty strings.";
        var examples = "Examples:\nWhat is the width of 6205? -> {\"product\":\"6205\",\"attribute\":\"width\"}\nHeight of 6205 N? -> {\"product\":\"6205 N\",\"attribute\":\"height\"}";
        var userPrompt = $"{examples}\nQuery: {query}\nReturn JSON only.";

        var payload = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = 80,
            response_format = new { type = "json_object" }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var client = _httpClientFactory.CreateClient("azure-openai");
        var resp = await client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            _logger.LogError("OpenAI request failed: {Status} {Error}", resp.StatusCode, err);
            return null;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        ExtractionResult? result = null;
        try
        {
            using var extracted = JsonDocument.Parse(content);
            string? product = extracted.RootElement.TryGetProperty("product", out var p) ? p.GetString() : null;
            string? attribute = extracted.RootElement.TryGetProperty("attribute", out var a) ? a.GetString() : null;

            if (!string.IsNullOrWhiteSpace(product))
            {
                product = product!.Trim();
            }
            if (!string.IsNullOrWhiteSpace(attribute))
            {
                attribute = attribute!.Trim();
            }
            result = new ExtractionResult { Product = product, Attribute = attribute };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse extraction JSON.");
        }
       
        var allowedAttributes = new HashSet<string>(new[]
        {
           
            "width","height","diameter","inner_diameter","outer_diameter",            
            "bore","id","od","b","d","inner","outer","inside","outside"
        }, StringComparer.OrdinalIgnoreCase);

        string? validatedProduct = result?.Product;
        string? validatedAttribute = result?.Attribute;

        var productRegex = new System.Text.RegularExpressions.Regex(@"\b([0-9]{3,6}(?:\s*[A-Za-z]+)?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (string.IsNullOrWhiteSpace(validatedProduct) || !productRegex.IsMatch(validatedProduct))
        {
            var m = productRegex.Match(query);
            if (m.Success) validatedProduct = m.Groups[1].Value.Trim();
        }

        if (string.IsNullOrWhiteSpace(validatedAttribute) || !allowedAttributes.Contains(validatedAttribute))
        {            
            var text = query.ToLowerInvariant();
            bool Matches(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (Matches(@"\b(width|\bB\b)\b")) validatedAttribute = "width";
            else if (Matches(@"\b(height|\bH\b)\b")) validatedAttribute = "height";
            else if (Matches(@"\b(od|outer\s*diameter|outside\s*diameter|outside)\b")) validatedAttribute = "outer_diameter";
            else if (Matches(@"\b(id|inner\s*diameter|inside\s*diameter|inside|inner|bore|\bd\b)\b")) validatedAttribute = "inner_diameter";
            else if (Matches(@"\bdiameter\b")) validatedAttribute = "diameter";
        }

        if (string.IsNullOrWhiteSpace(validatedProduct) || string.IsNullOrWhiteSpace(validatedAttribute))
        {
            return null;
        }

        return new ExtractionResult { Product = validatedProduct, Attribute = validatedAttribute };
    }
}


