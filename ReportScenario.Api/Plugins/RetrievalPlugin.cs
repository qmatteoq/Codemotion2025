using Azure.Identity;
using Microsoft.Agents.M365Copilot.Beta;
using Microsoft.Agents.M365Copilot.Beta.Models;
using Microsoft.Agents.M365Copilot.Beta.Copilot.Retrieval;
using System.ComponentModel;

namespace ReportScenario.Api.Plugins;

public class RetrievalPlugin : IRetrievalPlugin
{
    private string tenantId;

    private string clientId;

    private AgentsM365CopilotBetaServiceClient _client;
    private AuthenticationRecord authRecord;

    public bool IsClientInitialized => _client != null;

    public RetrievalPlugin(IConfiguration configuration)
    {
        tenantId = configuration["CopilotApi:TenantId"] ?? throw new InvalidOperationException("TenantId not configured");
        clientId = configuration["CopilotApi:ClientId"] ?? throw new InvalidOperationException("ClientId not configured");
    }

    public void Authenticate()
    {
        var scopes = new[] { "Files.Read.All", "Sites.Read.All" };


        var deviceCodeCredentialOptions = new DeviceCodeCredentialOptions
        {
            ClientId = clientId,
            TenantId = tenantId,
            // Callback function that receives the user prompt 
            // Prompt contains the generated device code that user must 
            // enter during the auth process in the browser 
            DeviceCodeCallback = (deviceCodeInfo, cancellationToken) =>
            {
                Console.WriteLine(deviceCodeInfo.Message);
                return Task.CompletedTask;
            },
        };

        //// https://learn.microsoft.com/dotnet/api/azure.identity.devicecodecredential 
        var deviceCodeCredential = new DeviceCodeCredential(deviceCodeCredentialOptions);
        var baseURL = "https://graph.microsoft.com/beta";

        _client = new AgentsM365CopilotBetaServiceClient(deviceCodeCredential, scopes, baseURL);
    }

    [Description("Get relevant pieces of information from the organizational data based on a prompt")]
    public async Task<List<string>> GetExtractsAsync([Description("The prompt of the user")] string prompt)
    {
        List<string> extracts = new List<string>();

        try
        {
            var requestBody = new RetrievalPostRequestBody
            {
                DataSource = RetrievalDataSource.SharePoint,
                QueryString = prompt,
                MaximumNumberOfResults = 10
            };

            var result = await _client.Copilot.Retrieval.PostAsync(requestBody);
            Console.WriteLine($"Retrieval post: {result}");

            if (result != null)
            {
                Console.WriteLine("Retrieval response received successfully");
                Console.WriteLine($"\nResults: {result.RetrievalHits.Count}");
                Console.WriteLine();
                if (result.RetrievalHits != null)
                {
                    foreach (var hit in result.RetrievalHits)
                    {
                        Console.WriteLine(hit.WebUrl);
                        if (hit.Extracts != null && hit.Extracts.Any())
                        {
                            foreach (var extract in hit.Extracts)
                            {
                                extracts.Add(extract.Text);
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No retrieval hits found in the response");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error making retrieval request: {ex.Message}");
            Console.Error.WriteLine(ex);
        }

        return extracts;
    }
}