using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using SKF.ProductAssistant.Functions.Models;
using SKF.ProductAssistant.Functions.Services;
using SKF.ProductAssistant.Functions.Storage;

namespace SKF.ProductAssistant.Functions.Functions;

public class QueryFunction
{
    private readonly ILogger<QueryFunction> _logger;
    private readonly IOpenAIExtractionService _openAI;
    private readonly IDataSheetRepository _repository;
    private readonly ICacheService _cache;

    public QueryFunction(ILogger<QueryFunction> logger, IOpenAIExtractionService openAI, IDataSheetRepository repository, ICacheService cache)
    {
        _logger = logger;
        _openAI = openAI;
        _repository = repository;
        _cache = cache;
    }

    [Function("ask")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "ask")] HttpRequestData req)
    {
        string? query = null;
        try
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("query", out var qProp))
                {
                    query = qProp.GetString();
                }
            }
        }
        catch { }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        if (string.IsNullOrWhiteSpace(query) || query.Length > 1000)
        {
            response.StatusCode = HttpStatusCode.BadRequest;
            await response.WriteStringAsync(JsonSerializer.Serialize(new { error = "Invalid input" }));
            return response;
        }

        string cacheKey = $"product:{query.Trim().ToLowerInvariant()}"; 
        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            await response.WriteStringAsync(cached);
            return response;
        }
      
        var extraction = await _openAI.ExtractProductAndAttributeAsync(query);
        if (extraction == null || string.IsNullOrWhiteSpace(extraction.Product) || string.IsNullOrWhiteSpace(extraction.Attribute))
        {
            await response.WriteStringAsync(JsonSerializer.Serialize(new { error = "Invalid input" }));
            return response;
        }
      
        var lookup = _repository.TryGetAttribute(extraction.Product!, extraction.Attribute!, out var value);
        if (!lookup)
        {
            var notFound = new { answer = $"I’m sorry, I can’t find that information for product {extraction.Product}." };
            await response.WriteStringAsync(JsonSerializer.Serialize(notFound));
            return response;
        }
        
        string answer = $"The {extraction.Attribute} of the {extraction.Product} bearing is {value}.";
        var payload = JsonSerializer.Serialize(new { answer });
       
        var finalKey = $"product:{extraction.Product!.ToLowerInvariant()}|attribute:{extraction.Attribute!.ToLowerInvariant()}";
        await _cache.SetStringAsync(finalKey, payload, TimeSpan.FromHours(24));
        await response.WriteStringAsync(payload);
        return response;
    }
}


