## SKF Product Assistant – Developer Guide

### Overview
Azure Functions (.NET 8, isolated worker) HTTP API that answers product attribute questions for SKF bearings. It extracts product + attribute from a free‑text query, looks up normalized attributes in local JSON datasheets, and returns a concise, validated answer. Caching avoids rework for repeated queries.

### Architecture
- Function entrypoint: `Functions/QueryFunction` (HTTP POST `/api/ask`)
- Services:
  - `IOpenAIExtractionService` → `OpenAIExtractionService`: turns query text into `{ product, attribute }` using Azure OpenAI with strict JSON output and regex fallbacks
  - `IDataSheetRepository` → `JsonDataSheetRepository`: loads/normalizes datasheets, provides lookups
  - `ICacheService` → `CacheService`: Redis or in‑memory string cache
- Composition root: `Program.cs` (DI, configuration, HttpClient factory)

### Key Design Decisions
1) Separation of concerns (SOLID)
   - `QueryFunction` orchestrates only; knows nothing about parsing files or calling OpenAI
   - `OpenAIExtractionService` only extracts intent
   - `JsonDataSheetRepository` only loads/normalizes/queries data
   - Dependencies are injected via interfaces (Dependency Inversion)

2) DRY normalization
   - `Services/NameNormalization.cs` centralizes two responsibilities:
     - `NormalizeAttribute`: maps synonyms (ID/OD/B/d/bore/inner/outer/width/height/diameter, C/C0, etc.) to canonical keys used by the repository
     - `NormalizeNameToKey`: converts human labels from datasheets (e.g., "Basic static load rating") into snake_case keys (e.g., `basic_static_load_rating`)
   - Repository uses the same normalization across all data sections to avoid duplication

3) YAGNI and correctness
   - Data stored as simple strings (e.g., "15mm", "14.8 kN"); no premature type system or unit conversions
   - No external database; local JSON files and optional Redis cache
   - Strict output: answers only what exists in datasheets; apologizes when absent

4) Robust extraction
   - Primary: Azure OpenAI (low temperature, `response_format: json_object`) to constrain output
   - Secondary: lightweight regex fallbacks for product codes and attribute synonyms, enabling offline/basic behavior
   - `IHttpClientFactory` is used for resilient HTTP usage and testability

### Data Handling
`JsonDataSheetRepository` supports two shapes:
1) Single product object
2) Array of product objects

It flattens common arrays into a single attribute dictionary per product:
- `dimensions` → `width`, `inner_diameter` (ID/bore), `outer_diameter` (OD), `diameter`
- `performance` → `reference_speed`, `limiting_speed`, `basic_dynamic_load_rating` (C), `basic_static_load_rating` (C0), etc.
- `properties` → `material_bearing`, `cage`, `bore_type`, `tolerance_class`, `radial_internal_clearance`, …
- `logistics` → `ean_code`, `pack_gross_weight`, `product_net_weight`, …
- `specifications` → generic key/value (e.g., `photo_url`)

Synonyms are normalized via `NameNormalization` so requests like "OD of 6205" and labels like "Outside diameter" both map to `outer_diameter`.

### Request → Response Flow
1) Client POSTs `{ "query": "What is the width of 6205?" }` to `/api/ask`
2) Extraction resolves to `{ product: "6205", attribute: "width" }`
3) Repository returns attribute value from normalized in‑memory map
4) Function replies `{ "answer": "The width of the 6205 bearing is 15mm." }` and caches the response

### Configuration
- `local.settings.json` or environment variables:
  - `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_KEY`, `AZURE_OPENAI_DEPLOYMENT`, `AZURE_OPENAI_API_VERSION`
  - `REDIS_CONNECTION` (optional)
  - `DATA_PATH` (defaults to `data` folder next to the function app)
- `SKF.ProductAssistant.Functions.csproj` copies `data/*.json` to output for local runs

### Running Locally
1) Install .NET 8 SDK and Azure Functions Core Tools v4
2) `cd src/SKF.ProductAssistant.Functions && dotnet restore && func start`
3) Test:
   - `curl -s -X POST http://localhost:7071/api/ask -H "Content-Type: application/json" -d '{"query":"OD for 6205 N?"}'`

### Examples Supported
- Dimensions: Width/B, Bore/ID/d/Inner, OD/Outside/Outer, Diameter
- Performance: Reference speed, Limiting speed, C (basic dynamic), C0 (basic static)
- Properties: Material, Cage, Bore type, Tolerance class, Radial internal clearance, …
- Logistics: EAN code, Pack gross weight/height/length/width/volume, Product net weight, …

### Testing and Extensibility
- Add new synonyms only in `NameNormalization`
- To support new datasheet sections, extend the repository by following existing flattening patterns
- For large datasets, swap `JsonDataSheetRepository` with an indexed repository behind the same `IDataSheetRepository` interface

### Security Notes
- Do not commit secrets; use environment variables
- All outputs are plain strings; no code execution or templating


