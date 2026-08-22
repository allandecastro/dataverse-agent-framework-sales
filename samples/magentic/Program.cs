#pragma warning disable MAAIW001 // Magentic workflow types are experimental in Agent Framework 1.17.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Dataverse;
using Zava.SalesIntelligence.Dataverse.Tools;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

// The same squad, autonomous orchestration. Instead of the fixed workflow graph, a Magentic
// manager plans the work and delegates to the participant agents on its own, round by round,
// until it decides the question is answered.
//
// This is the other end of the spectrum from src/Zava.SalesIntelligence.Workflow. There the
// order, the superstep barrier, the deterministic Ranker, and the approval gate are all things
// we placed by hand. Here the manager decides who speaks next. It is flexible and it is
// impressive, and it is also non-deterministic and hard to gate - which is exactly why the
// demo builds the harness by hand instead. Read-only: no writes, no approval gate.

string dataverseUrl = Require("ZAVA_DATAVERSE_URL");
string openAiEndpoint = Require("ZAVA_OPENAI_ENDPOINT");
string fastDeployment = Require("ZAVA_OPENAI_DEPLOYMENT");
string reasoningDeployment = Require("ZAVA_OPENAI_REASONING_DEPLOYMENT");

var credential = new AzureCliCredential();
using var client = DataverseClientFactory.Create(dataverseUrl, credential);

Guid? sellerUserId = Guid.TryParse(Environment.GetEnvironmentVariable("ZAVA_SELLER_USER_ID"), out var seller) ? seller : null;
var queries = new WatcherQueries(client, sellerUserId);
var accountTools = new AccountTools(queries);

AIFunction getOpportunities = AIFunctionFactory.Create(
    queries.GetOpenOpportunitiesAsync,
    name: "get_open_opportunities",
    description: "Get the open opportunities for one account. Pass the account id from get_accounts.");
AIFunction getActivities = AIFunctionFactory.Create(
    (Guid accountId, CancellationToken ct) => queries.GetActivitiesAsync(accountId, 90, ct),
    name: "get_recent_activities",
    description: "Get the recent activities for one account. Pass the account id from get_accounts.");

var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential);
ChatClient Fast() => openAiClient.GetChatClient(fastDeployment);
ChatClient Reasoning() => openAiClient.GetChatClient(reasoningDeployment);

// The manager plans and delegates. The participants each do one job, the way the squad does -
// but nothing here forces the order.
AIAgent manager = Reasoning().AsAIAgent(
    name: "Manager",
    instructions: "You coordinate a small sales team to answer the seller's question. Delegate retrieval, assessment, and action planning to your participants, then give a final recommendation grounded in what they found.");

// The manager routes by each participant's description, so every participant needs one.
AIAgent retriever = Fast().AsAIAgent(
    name: "Retriever",
    instructions: "You retrieve a seller's pipeline from Dataverse with your tools. Call get_accounts first, then the per-account reads. Report the records you found.",
    description: "Retrieves the seller's pipeline from Dataverse: accounts, open opportunities, and recent activities.",
    tools: [accountTools.GetAccountsFunction(), getOpportunities, getActivities]);

AIAgent assessor = Reasoning().AsAIAgent(
    name: "Assessor",
    instructions: "You assess each open opportunity against BANT (budget, authority, need, timing) using only the retrieved records, and cite the record behind each judgement.",
    description: "Judges retrieved opportunities against BANT and cites the evidence behind each axis.");

AIAgent strategist = Fast().AsAIAgent(
    name: "Strategist",
    instructions: "You propose the next best action for the most qualified opportunities, tied to a specific signal the assessor cited. Do not invent evidence.",
    description: "Proposes the next best action for the most qualified opportunities.");

var workflow = AgentWorkflowBuilder.CreateMagenticBuilderWith(manager)
    .AddParticipants([retriever, assessor, strategist])
    .RequirePlanSignoff(false)
    .WithMaxRounds(20)
    .WithMaxStalls(3)
    .Build();

Console.WriteLine("Handing the question to a Magentic manager and letting it orchestrate...");
Console.WriteLine();

// The Magentic driver: hand the goal in as a chat message, then send a TurnToken to pump the
// orchestrator through its rounds. The TurnToken is what makes it delegate - without it the
// orchestrator runs one superstep and stops. The terminal WorkflowOutputEvent carries the
// manager's synthesized answer as the conversation transcript.
await using var run = await InProcessExecution.RunStreamingAsync(
    workflow, new List<ChatMessage> { new(ChatRole.User, "What should I focus on this week?") });
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

string? lastResponseId = null;
WorkflowOutputEvent? finalOutput = null;
await foreach (var evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case AgentResponseUpdateEvent update:
            // Stream each participant's text, printing the speaker once per contiguous turn.
            var responseId = update.Update.ResponseId ?? update.Update.MessageId ?? update.ExecutorId;
            if (!string.Equals(responseId, lastResponseId, StringComparison.Ordinal))
            {
                Console.WriteLine();
                Console.Write($"- {update.ExecutorId}: ");
                lastResponseId = responseId;
            }
            Console.Write(update.Update.Text);
            break;

        case MagenticPlanCreatedEvent plan:
            Console.WriteLine($"\n[plan]\n{plan.FullTaskLedger.Text}");
            break;

        case MagenticProgressLedgerUpdatedEvent progress:
            Console.WriteLine($"\n[ledger] next={progress.ProgressLedger.NextSpeaker} satisfied={progress.ProgressLedger.IsRequestSatisfied}");
            break;

        case WorkflowOutputEvent output when output.Is<List<ChatMessage>>():
            finalOutput = output;
            break;

        case WorkflowErrorEvent error:
            Console.Error.WriteLine(error.Exception?.ToString() ?? "workflow error");
            break;
    }
}

if (finalOutput?.As<List<ChatMessage>>() is { Count: > 0 } transcript)
{
    Console.WriteLine("\n\n=== Final recommendation ===\n");
    Console.WriteLine(transcript[^1].Text);
}

static string Require(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
