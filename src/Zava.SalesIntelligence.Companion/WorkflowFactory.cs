using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Agents;
using Zava.SalesIntelligence.Dataverse;
using Zava.SalesIntelligence.Workflow;

namespace Zava.SalesIntelligence.Companion;

// Builds the sales workflow from the ZAVA_* configuration. Retrieval runs under the
// reader credential and writes under the writer credential, so the companion shows the
// same identity boundary as the console host.
public sealed class WorkflowFactory
{
    public Microsoft.Agents.AI.Workflows.Workflow Build()
    {
        var dataverseUrl = Require("ZAVA_DATAVERSE_URL");
        var openAiEndpoint = Require("ZAVA_OPENAI_ENDPOINT");
        var reasoningDeployment = Require("ZAVA_OPENAI_REASONING_DEPLOYMENT");

        var credential = new AzureCliCredential();
        var readerCredential = AppUserCredential("ZAVA_READER_CLIENT_ID", "ZAVA_READER_CLIENT_SECRET", credential);
        var writerCredential = AppUserCredential("ZAVA_WRITER_CLIENT_ID", "ZAVA_WRITER_CLIENT_SECRET", credential);

        var readerClient = DataverseClientFactory.Create(dataverseUrl, readerCredential);
        var writerClient = DataverseClientFactory.Create(dataverseUrl, writerCredential);
        // Scope the account read to the seller by owner when running under the reader app
        // identity (which owns no accounts); falls back to the signed-in user when unset.
        Guid? sellerUserId = Guid.TryParse(Environment.GetEnvironmentVariable("ZAVA_SELLER_USER_ID"), out var seller) ? seller : null;
        var queries = new WatcherQueries(readerClient, sellerUserId);
        var watcher = new WatcherService(queries);
        var writer = new WriterTools(writerClient);

        var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential);
        AIAgent thinker = openAiClient.GetChatClient(reasoningDeployment)
            .AsAIAgent(name: "Thinker", instructions: Thinker.BuildSystemPrompt());
        // The Operator runs on the reasoning deployment: the fast model under-produced
        // actions (some actionable accounts got none, coverage swung run to run). Reasoning
        // gives it the multi-step consistency the action generation needs.
        AIAgent operatorAgent = openAiClient.GetChatClient(reasoningDeployment)
            .AsAIAgent(name: "Operator", instructions: Operator.BuildSystemPrompt());

        var pacing = TimeSpan.FromMilliseconds(
            int.TryParse(Environment.GetEnvironmentVariable("ZAVA_STEP_DELAY_MS"), out var ms) ? ms : 0);

        return SalesWorkflow.Build(queries, watcher, thinker, operatorAgent, writer, pacing);
    }

    static Azure.Core.TokenCredential AppUserCredential(string clientIdVar, string clientSecretVar, Azure.Core.TokenCredential fallback)
    {
        var clientId = Environment.GetEnvironmentVariable(clientIdVar);
        var clientSecret = Environment.GetEnvironmentVariable(clientSecretVar);
        var tenantId = Environment.GetEnvironmentVariable("ZAVA_TENANT_ID");

        return clientId is not null && clientSecret is not null && tenantId is not null
            ? new ClientSecretCredential(tenantId, clientId, clientSecret)
            : fallback;
    }

    static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
}
