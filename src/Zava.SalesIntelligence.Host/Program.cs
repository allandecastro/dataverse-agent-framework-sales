using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Dataverse;
using Zava.SalesIntelligence.Dataverse.Tools;

// Phase 1: one agent, one Dataverse tool, console host. Configuration comes from
// environment variables so no environment-specific values live in the repo.
string dataverseUrl = RequireEnv("ZAVA_DATAVERSE_URL");
string openAiEndpoint = RequireEnv("ZAVA_OPENAI_ENDPOINT");
string deployment = RequireEnv("ZAVA_OPENAI_DEPLOYMENT");

// Phase 1 runs under the interactive az session. Application users come in phase 4.
var credential = new AzureCliCredential();

using var dataverse = DataverseClientFactory.Create(dataverseUrl, credential);
var accountTools = new AccountTools(dataverse);

AIAgent watcher = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
    .GetChatClient(deployment)
    .AsAIAgent(
        name: "Watcher",
        instructions: """
            You retrieve Dataverse data for a seller and answer using the tools.
            Do not guess. If a tool returns nothing, say so plainly.
            """,
        tools: [accountTools.GetAccountsFunction()]);

string question = args.Length > 0 ? string.Join(' ', args) : "How many accounts do I have?";

Console.WriteLine($"Q: {question}");
var answer = await watcher.RunAsync(question);
Console.WriteLine($"A: {answer}");

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
