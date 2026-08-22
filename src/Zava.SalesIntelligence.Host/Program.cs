using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using OpenAI.Chat;
using Zava.SalesIntelligence.Agents;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;
using Zava.SalesIntelligence.Workflow;

// Phase 3 and 4: the squad as a workflow graph, with an approval gate. Watcher retrieves,
// Thinker judges, Ranker orders, Operator prepares actions, the run pauses for approval,
// and approved actions are written. Stream the supersteps.
string dataverseUrl = RequireEnv("ZAVA_DATAVERSE_URL");
string openAiEndpoint = RequireEnv("ZAVA_OPENAI_ENDPOINT");
string reasoningDeployment = RequireEnv("ZAVA_OPENAI_REASONING_DEPLOYMENT");
var credential = new AzureCliCredential();

// The identity boundary. Retrieval runs under the reader credential, writes under the
// writer credential, injected here - the executors never see which one they hold. Set
// the app-user secrets to make it real; until then both fall back to the interactive
// login. See docs/identity.md.
var readerCredential = AppUserCredential("ZAVA_READER_CLIENT_ID", "ZAVA_READER_CLIENT_SECRET", credential);
var writerCredential = AppUserCredential("ZAVA_WRITER_CLIENT_ID", "ZAVA_WRITER_CLIENT_SECRET", credential);

using var readerClient = DataverseClientFactory.Create(dataverseUrl, readerCredential);
using var writerClient = DataverseClientFactory.Create(dataverseUrl, writerCredential);
// The seller whose pipeline to read. When the Watcher runs under the reader app identity
// (which owns no accounts) this scopes the account query to the seller by owner. Unset for
// an interactive run, where the query falls back to the signed-in user.
Guid? sellerUserId = Guid.TryParse(Environment.GetEnvironmentVariable("ZAVA_SELLER_USER_ID"), out var seller) ? seller : null;
var queries = new WatcherQueries(readerClient, sellerUserId);
var watcher = new WatcherService(queries);
var writer = new WriterTools(writerClient);

var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential);
AIAgent thinker = openAiClient.GetChatClient(reasoningDeployment)
    .AsAIAgent(name: "Thinker", instructions: Thinker.BuildSystemPrompt());
// The Operator runs on the reasoning deployment: the fast model under-produced actions
// (some actionable accounts got none, coverage swung run to run). Reasoning gives it the
// multi-step consistency the action generation needs.
AIAgent operatorAgent = openAiClient.GetChatClient(reasoningDeployment)
    .AsAIAgent(name: "Operator", instructions: Operator.BuildSystemPrompt());

var pacing = TimeSpan.FromMilliseconds(
    int.TryParse(Environment.GetEnvironmentVariable("ZAVA_STEP_DELAY_MS"), out var ms) ? ms : 0);
var workflow = SalesWorkflow.Build(queries, watcher, thinker, operatorAgent, writer, pacing);

// `dotnet run ... graph` prints the workflow graph as Mermaid and exits, without running it.
if (args.Contains("graph"))
{
    Console.WriteLine(workflow.ToMermaidString());
    return;
}

Console.WriteLine("Running the sales workflow...");
Console.WriteLine();

var run = await InProcessExecution.RunStreamingAsync(workflow, "What should I focus on this week?");
await foreach (var workflowEvent in run.WatchStreamAsync())
{
    switch (workflowEvent)
    {
        case ExecutorCompletedEvent completed:
            Console.WriteLine($"  superstep: {completed.ExecutorId} done");
            break;

        case RequestInfoEvent requestInfo:
            var approvalRequest = requestInfo.Request.Data.As<ApprovalRequest>();
            if (approvalRequest is not null)
            {
                var approved = Approve(approvalRequest);
                await run.SendResponseAsync(requestInfo.Request.CreateResponse(new ApprovalDecision(approved)));
            }
            break;

        case WorkflowOutputEvent { Data: WriteReport report }:
            Console.WriteLine();
            foreach (var line in report.Lines)
            {
                Console.WriteLine($"  {line}");
            }
            break;
    }
}

// The seller's decision. Interactive prompting is a companion concern; here the policy
// comes from ZAVA_APPROVE: "all", "none" (default), or a comma-separated list of numbers.
static IReadOnlyList<ActionProposal> Approve(ApprovalRequest request)
{
    Console.WriteLine();
    Console.WriteLine("Approval needed. Ranked focus:");
    foreach (var recommendation in request.Recommendations)
    {
        var assessment = recommendation.Assessment;
        Console.WriteLine($"  #{recommendation.Rank}  {assessment.AccountName}  [{assessment.Archetype}]  bant={assessment.Bant.Score}");
    }

    Console.WriteLine("Proposed actions:");
    foreach (var proposal in request.Proposals)
    {
        Console.WriteLine($"  [{proposal.Number}] {proposal.Action.Type} on {proposal.OpportunityName}: {proposal.Action.Title}");
    }

    var policy = Environment.GetEnvironmentVariable("ZAVA_APPROVE");
    if (policy is "all")
    {
        return request.Proposals;
    }
    if (string.IsNullOrWhiteSpace(policy) || policy is "none")
    {
        return [];
    }

    var numbers = policy
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(int.Parse)
        .ToHashSet();
    return request.Proposals.Where(proposal => numbers.Contains(proposal.Number)).ToList();
}

// Builds an application-user credential from its client id and secret, or falls back to
// the interactive login when they are not set. The tenant is shared (ZAVA_TENANT_ID).
static TokenCredential AppUserCredential(string clientIdVar, string clientSecretVar, TokenCredential fallback)
{
    var clientId = Environment.GetEnvironmentVariable(clientIdVar);
    var clientSecret = Environment.GetEnvironmentVariable(clientSecretVar);
    var tenantId = Environment.GetEnvironmentVariable("ZAVA_TENANT_ID");

    return clientId is not null && clientSecret is not null && tenantId is not null
        ? new ClientSecretCredential(tenantId, clientId, clientSecret)
        : fallback;
}

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
