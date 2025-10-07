using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ReportScenario.Api.Plugins;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

const string SourceName = "OpenTelemetryAspire.ConsoleApp";
const string ServiceName = "AgentOpenTelemetry";

// Create a resource to identify this service
var resource = ResourceBuilder.CreateDefault()
    .AddService(ServiceName, serviceVersion: "1.0.0")
    .AddAttributes(new Dictionary<string, object>
    {
        ["service.instance.id"] = Environment.MachineName,
        ["deployment.environment"] = "development"
    })
    .Build();

// Configure OpenTelemetry for Aspire dashboard
var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4318";

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName, serviceVersion: "1.0.0"))
    .AddSource(SourceName) // Our custom activity source
    .AddSource("*Microsoft.Agents.AI") // Agent Framework telemetry
    .AddHttpClientInstrumentation() // Capture HTTP calls to OpenAI
    .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint))
    .Build();

// Setup metrics with resource and instrument name filtering
using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName, serviceVersion: "1.0.0"))
    .AddMeter(SourceName) // Our custom meter
    .AddMeter("*Microsoft.Agents.AI") // Agent Framework metrics
    .AddHttpClientInstrumentation() // HTTP client metrics
    .AddRuntimeInstrumentation() // .NET runtime metrics
    .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint))
    .Build();

// Setup structured logging with OpenTelemetry
var serviceCollection = new ServiceCollection();
serviceCollection.AddLogging(loggingBuilder => loggingBuilder
    .SetMinimumLevel(LogLevel.Debug)
    .AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName, serviceVersion: "1.0.0"));
        options.AddOtlpExporter(otlpOptions => otlpOptions.Endpoint = new Uri(otlpEndpoint));
        options.IncludeScopes = true;
        options.IncludeFormattedMessage = true;
    }));

using var activitySource = new ActivitySource(SourceName);
using var meter = new Meter(SourceName);

var foundryEndpoint= builder.Configuration["FoundryEndpoint"] ?? throw new InvalidOperationException("Foundry endpoint not configured");

var client = new AzureOpenAIClient(
    new Uri(foundryEndpoint),
    new AzureCliCredential())
        .GetChatClient("gpt-4.1")
        .AsIChatClient()
        .AsBuilder()
        .UseFunctionInvocation()
        .UseOpenTelemetry(sourceName: SourceName, configure: (cfg) => cfg.EnableSensitiveData = true)
        .Build();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddChatClient(client);

builder.AddAIAgent("MarketResearcher", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    return new ChatClientAgent(client,
        name: key,
        instructions:
        """
        You are a market researcher agent. The user will ask you to prepare a report about the products launched by a given a tech company. You must use **only** public data to prepare the report. Use it to get relevant pieces of information about the topic the user is asking you to prepare a report about.
        """
    ).AsBuilder()
    .UseOpenTelemetry(SourceName)
    .Build();
});

builder.AddAIAgent("EnterpriseResearcher", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();

    RetrievalPlugin retrievalPlugin = new RetrievalPlugin(builder.Configuration);
    retrievalPlugin.Authenticate();

    return new ChatClientAgent(client,
        name: key,
        instructions:
        """
        You are a researcher agent. You will receive in input a report about a tech company. You must enhance it with specific information related to the Contoso organization. You must use **only** organizational data to prepare the report. You have access to a tool that gives you access to organizational data about Contoso. Use it to get relevant pieces of information about the topic the user is asking you to prepare a report about.
        """,
        tools: [AIFunctionFactory.Create(retrievalPlugin.GetExtractsAsync)]
    ).AsBuilder()
    .UseOpenTelemetry(SourceName)
    .Build();;
});

[Description("Return the price of the given stock")]
string GetStockPrice(string companyName) =>
    $"Title: {companyName} - Price: 500 $";

builder.AddAIAgent("StockPriceAgent", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    return new ChatClientAgent(client,
        name: key,
        instructions:
        """
        You are a stock price agent. The user will ask you to provide the stock price of a given company. You have access to a tool that provides you the stock price of a given company. Use it to get the stock price of the company the user is asking you about.
        """,
        tools: [AIFunctionFactory.Create(GetStockPrice)]
    ).AsBuilder()
    .UseOpenTelemetry(SourceName)
    .Build();;
});

var app = builder.Build();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var appLogger = loggerFactory.CreateLogger<Program>();

app.MapDefaultEndpoints();



// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/agent/chat", async (
    [FromKeyedServices("MarketResearcher")] AIAgent marketResearcher,
    [FromKeyedServices("EnterpriseResearcher")] AIAgent organizationalAgent,
    [FromKeyedServices("StockPriceAgent")] AIAgent stockPriceAgent,
    string prompt) =>
    {
        var workflow = AgentWorkflowBuilder.BuildSequential(marketResearcher, stockPriceAgent);
        var workflowAgent = await workflow.AsAgentAsync();
        var thread = workflowAgent.GetNewThread();
        var response = await workflowAgent.RunAsync(prompt);
        return Results.Ok(response.Text);
    }
);

app.Run();