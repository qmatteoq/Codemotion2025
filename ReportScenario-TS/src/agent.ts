import { Activity, ActivityTypes } from "@microsoft/agents-activity";
import { AgentApplication, MemoryStorage, TurnContext } from "@microsoft/agents-hosting";

// Define storage and application
const storage = new MemoryStorage();
export const agentApp = new AgentApplication({
  storage,
});

agentApp.onConversationUpdate("membersAdded", async (context: TurnContext) => {
  await context.sendActivity(`Hi there! I'm an agent to chat with you.`);
});

// Listen for ANY message to be received. MUST BE AFTER ANY OTHER MESSAGE HANDLERS
agentApp.onActivity(ActivityTypes.Message, async (context: TurnContext) => {
  console.log(`Received message: ${context.activity.text}`);

  const typing = Activity.fromObject({ type: ActivityTypes.Typing });

   await context.sendActivity(typing);
  process.env["NODE_TLS_REJECT_UNAUTHORIZED"] = "0";
  
  const apiEndpoint = process.env["services__reportscenario-api__http__0"];

  if (!apiEndpoint) {
    await context.sendActivity("API endpoint not configured.");
    return;
  }
  const url = `${apiEndpoint}/agent/chat?prompt=${encodeURIComponent(context.activity.text)}`;
  console.log(`Calling API: ${url}`);
  // Set up fetch timeout using AbortController
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 600000); // 10 minutes
  try {
    const response = await fetch(url, { method: "GET", signal: controller.signal });
    clearTimeout(timeout);
    if (!response.ok) {
      await context.sendActivity(`API error: ${response.statusText}`);
      return;
    }
    const answer = await response.text();
    await context.sendActivity(answer);
  } catch (err) {
    console.log(`Error calling API: ${err}`);
    await context.sendActivity(`API call failed: ${err}`);
  }
});
