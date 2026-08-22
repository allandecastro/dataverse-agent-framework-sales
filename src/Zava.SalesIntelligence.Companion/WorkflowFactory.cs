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
// The built workflow together with the Dataverse clients it holds. The caller disposes
// Resources when the run is done - the ServiceClients are IDisposable and outlive Build(),
// so, unlike the console host's `using`, they need an explicit owner here.
public sealed record BuiltWorkflow(Microsoft.Agents.AI.Workflows.Workflow Workflow, IReadOnlyList<IDisposable> Resources);

public sealed class WorkflowFactory
{
    public BuiltWorkflow Build()
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

        var workflow = SalesWorkflow.Build(queries, watcher, thinker, operatorAgent, writer, pacing);
        return new BuiltWorkflow(workflow, [readerClient, writerClient]);
    }

    // A grounded question-answer agent over one run's results. The analysis is baked into the
    // instructions, so the agent can only speak to what the squad produced - it has no tools,
    // no Dataverse, and cannot re-judge or write. Runs on the reasoning deployment for the
    // finer questions.
    public PipelineConversation BuildConversation(string analysisJson)
    {
        var openAiEndpoint = Require("ZAVA_OPENAI_ENDPOINT");
        var reasoningDeployment = Require("ZAVA_OPENAI_REASONING_DEPLOYMENT");
        var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), new AzureCliCredential());

        var instructions = ConversationInstructions + "\n\nThis week's analysis (JSON):\n" + analysisJson;
        AIAgent assistant = openAiClient.GetChatClient(reasoningDeployment).AsAIAgent(name: "Assistant", instructions: instructions);
        return new PipelineConversation(assistant);
    }

    const string ConversationInstructions =
        """
        You are the sales squad's spokesperson. Answer the seller's questions about this
        week's pipeline analysis, using ONLY the analysis provided below.

        - Ground every answer in that data. Name the account and cite the evidence - the
          record and its date - behind any claim.
        - If the seller asks about something not in the analysis, say you do not have it. Never
          guess or invent a score, an action, a date, or a contact detail.
        - You do not judge or re-score, and you never write anything to the CRM. You only
          explain and summarise what the squad already found.
        - Be concise and specific, and read well out loud.
        """;

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
