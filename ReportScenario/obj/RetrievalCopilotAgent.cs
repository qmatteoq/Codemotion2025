using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace RetrievalAgent.Bot.Agents;

public class RetrievalCopilotAgent
{
    private readonly Kernel _kernel;
    private readonly ChatCompletionAgent _agent;

    private const string AgentName = "RetrievalAgent";
    private const string AgentInstructions = """
        You are a friendly assistant that helps people by answering questions about the IT policies of the Contoso company.
        You have access to a tool that gives you access to the organizational data of the Contoso company, which includes the IT policies of the company.
        You must use it to get extracts from the IT policies of the Contoso company and then leverage that information to answer the user's question.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetrievalCopilotAgent"/> class.
    /// </summary>
    /// <param name="kernel">An instance of <see cref="Kernel"/> for interacting with an LLM.</param>
    public RetrievalCopilotAgent(Kernel kernel, IServiceProvider service, IRetrievalPlugin retrievalPlugin)
    {
        _kernel = kernel;

        // Define the agent
        _agent =
            new()
            {
                Instructions = AgentInstructions,
                Name = AgentName,
                Kernel = _kernel,
                Arguments = new KernelArguments(new OpenAIPromptExecutionSettings() 
                { 
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                }),
            };

        // Give the agent some tools to work with
        _agent.Kernel.Plugins.AddFromObject(retrievalPlugin, serviceProvider: service);
    }

    public async Task<string> InvokeAgentAsync(string input, ChatHistory chatHistory)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);
        AgentThread thread = new ChatHistoryAgentThread();
        ChatMessageContent message = new(AuthorRole.User, input);
        chatHistory.Add(message);

        StringBuilder sb = new();
        await foreach (ChatMessageContent response in this._agent.InvokeAsync(chatHistory, thread: thread))
        {
            chatHistory.Add(response);
            sb.Append(response.Content);
        }

        // Make sure the response is in the correct format and retry if necessary
        try
        {
            string resultContent = sb.ToString();
            return resultContent;
        }
        catch (Exception exc)
        {
            return await InvokeAgentAsync($"That response did not match the expected format. Please try again. Error: {exc.Message}", chatHistory);
        }
    }
}
