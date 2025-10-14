using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Azure.AI.OpenAI;
using Azure.Identity;
using ModelContextProtocol.Client;
using OpenAI;
using Azure.AI.Agents.Persistent;
using Microsoft.Agents.AI.Workflows;
using ReportScenario.Plugins;
using System.ComponentModel;

using Microsoft.Extensions.Configuration;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .Build();

var openAIEndpoint = configuration["OpenAIEndpoint"] ?? throw new InvalidOperationException("Foundry endpoint not configured");

//setting up the client

var client = new AzureOpenAIClient(
    new Uri(openAIEndpoint),
    new AzureCliCredential())
        .GetChatClient("gpt-4.1").AsIChatClient();


var alphaVantageMcpUrl = configuration["AlphaVantage:Endpoint"] ?? throw new InvalidOperationException("Alpha Vantage MCP endpoint not configured");
var alphaVantageApiKey = configuration["AlphaVantage:ApiKey"] ?? throw new InvalidOperationException("Alpha Vantage API key not configured");


RetrievalPlugin retrievalPlugin = new RetrievalPlugin(configuration);
retrievalPlugin.Authenticate();

//setting up the stock agent

await using var localMcpClient = await McpClient.CreateAsync(new StdioClientTransport(new()
{
    Name = "Alpha",
    Command = "uvx",
    Arguments = ["av-mcp", alphaVantageApiKey]
}));

var mcpTools = await localMcpClient.ListToolsAsync();

[Description("Return the price of the given stock")]
string GetStockPrice(string companyName) =>
    $"Title: {companyName} - Price: 500 $";

var stockAgent = new ChatClientAgent(client,
    new ChatClientAgentOptions
    {
        Instructions = "You are a financial analyst. Use Alpha Vantage data to answer questions about stocks, forex, and other financial markets. You will get in input a report about the products created by a tech organization. You must use **only** the tools available to you to get the latest stock price of the organization and add it to the report. You must generate, as output, the full report you received in input, plus the stock price information you found. If you don't find any stock price information, just return the report you received in input.",
        Name = "Stock Generator",
        ChatOptions = new ChatOptions
        {
            Tools = [.. mcpTools.Cast<AITool>()],
            // Tools = [AIFunctionFactory.Create(GetStockPrice)],
            ResponseFormat = ChatResponseFormat.Text
        }
    });

//setting up the researcher agent

string foundryEndpoint = configuration["FoundryEndpoint"] ?? throw new InvalidOperationException("Foundry endpoint not configured");

var persistentAgentsClient = new PersistentAgentsClient(foundryEndpoint, new AzureCliCredential());
var knowledgeAgent = await persistentAgentsClient.GetAIAgentAsync("asst_LcTEycPXtHWg3oUMi0KeXGEu");

//setting up the enterprise researcher agent

var organizationalAgent = new ChatClientAgent(client,
    new ChatClientAgentOptions
    {
        Instructions = "You are a researcher agent. You will receive in input a report about a tech company. You must enhance it with specific information related to the Contoso organization. You must use **only** organizational data to prepare the report. You have access to a tool that gives you access to organizational data about Contoso. Use it to get relevant pieces of information about the topic the user is asking you to prepare a report about.",
        Name = "Enterprise Report Generator",
        ChatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(retrievalPlugin.GetExtractsAsync)]
        }
    });


var workflow = AgentWorkflowBuilder.BuildSequential(knowledgeAgent, organizationalAgent, stockAgent);


var workflowAgent = await workflow.AsAgentAsync("report-agent", "Report agent");


var thread = workflowAgent.GetNewThread();


string? previousAuthor = null;
await foreach (var update in workflowAgent.RunStreamingAsync("Give me an overview of the key products launched by Microsoft in the last 5 years.", thread))
{
    if (previousAuthor != update.AuthorName)
    {
        // Print a new line with author and role
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"{update.AuthorName} - {update.Role}");
        previousAuthor = update.AuthorName;
    }
    // Print only the update property
    Console.Write(update);
}