using Microsoft.Teams.Api.Activities;
using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Activities;
using Microsoft.Teams.Apps.Annotations;

namespace ReportScenarioAgent.Controllers
{
    [TeamsController]
    public class Controller(IConfiguration configuration)
    {
        [Message]
        public async Task OnMessage([Context] MessageActivity activity, [Context] IContext.Client client, [Context] Microsoft.Teams.Common.Logging.ILogger log)
        {
            log.Info("hit!");
            await client.Typing();

            var apiEndpoint = configuration["services:reportscenario-api:http:0"];
            HttpClient httpClient = new() { BaseAddress = new Uri(apiEndpoint!), Timeout = TimeSpan.FromMinutes(10) };
            var response = await httpClient.GetStringAsync("/agent/chat?prompt=" + Uri.EscapeDataString(activity.Text));

            await client.Send(response);
        }

        [Conversation.MembersAdded]
        public async Task OnMembersAdded(IContext<ConversationUpdateActivity> context)
        {
            var welcomeText = "How can I help you today?";
            foreach (var member in context.Activity.MembersAdded)
            {
                if (member.Id != context.Activity.Recipient.Id)
                {
                    await context.Send(welcomeText);
                }
            }
        }

    }
}