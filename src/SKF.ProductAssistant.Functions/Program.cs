using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SKF.ProductAssistant.Functions.Services;
using SKF.ProductAssistant.Functions.Storage;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddLogging();
        services.AddMemoryCache();

        services.AddHttpClient("azure-openai", client =>
        {            
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<IOpenAIExtractionService, OpenAIExtractionService>();

        services.AddSingleton<IDataSheetRepository>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<JsonDataSheetRepository>>();

            string? dataDir = configuration["DATA_PATH"] ?? configuration["DATASHEET_DIRECTORY"];
            if (string.IsNullOrWhiteSpace(dataDir))
            {
                dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            }

            return new JsonDataSheetRepository(dataDir!, logger);
        });
    })
    .Build();

await host.RunAsync();


