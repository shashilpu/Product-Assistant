## SKF Product Assistant Mini (Azure Functions .NET 8)

### Prerequisites
- .NET 8 SDK
- Azure Functions Core Tools v4
- Azure OpenAI access (endpoint, key, deployment)
- Optional: Azure Redis Cache (connection string)

### Quick Start
1. Set environment variables (recommended) or edit `local.settings.json` values:
   - `AZURE_OPENAI_ENDPOINT`
   - `AZURE_OPENAI_KEY`
   - `AZURE_OPENAI_DEPLOYMENT` (e.g., `gpt-4o-mini`)
   - `AZURE_OPENAI_API_VERSION` (e.g., `2024-08-01-preview`)
   - `REDIS_CONNECTION_STRING` (optional)
   - `DATASHEET_DIRECTORY` (defaults to `data` under the function app directory)

2. Restore and run locally:
```bash
cd skf-product-assistant/src/SKF.ProductAssistant.Functions
dotnet restore
func start
```

3. Call the HTTP endpoint:
```bash
curl -s "http://localhost:7071/api/query?q=What%20is%20the%20width%20of%206205%3F"
```

or POST JSON:
```bash
curl -s -X POST http://localhost:7071/api/query \
  -H "Content-Type: application/json" \
  -d '{"query":"Height of 6205 N?"}'
```

### Project Structure
- `Functions/QueryFunction.cs`: HTTP trigger that accepts the query, orchestrates extraction, lookup, and caching.
- `Services/OpenAIExtractionService.cs`: Calls Azure OpenAI to extract product and attribute (JSON output, temperature 0.1).
- `Storage/DataSheetRepository.cs`: Loads local JSON datasheets and provides attribute lookup with basic normalization.
- `Services/CacheService.cs`: Redis-backed cache with in-memory fallback.
- `local.settings.json`: Local configuration for development only (do not commit secrets).
- `data/*.json`: Sample datasheet files (add your two provided files here).

### Developer Docs
- See `DEVELOPER_GUIDE.md` for architecture, design rationale (SOLID/DRY/YAGNI), normalization rules, and extensibility notes.

### Datasheet Format
Supported shapes:
1) Array of products
```json
[
  { "product": "6205", "width": "15mm", "height": "...", "diameter": "..." },
  { "product": "6205 N", "width": "15mm" }
]
```
2) Single product object
```json
{ "product": "6205", "width": "15mm", "inner_diameter": "25mm", "outer_diameter": "52mm" }
```

Attribute normalization includes common synonyms: `b`→`width`, `id`/`bore`→`inner_diameter`, `od`→`outer_diameter`, etc.

### Hallucination Reduction
- Temperature set low (0.1) and `response_format: json_object` to constrain extraction.
- Final answers are validated strictly against local datasheets; if attribute not found, returns a clear apology message.

### Security & Config
- Use environment variables in production; avoid storing keys in source.
- Input is treated as plain text; output escapes JSON. No dynamic evaluation.

### Testing Notes
- For repeated queries, caching avoids re-calling OpenAI and re-reading files.
- Clear cache by restarting the function app or expiring Redis keys.


