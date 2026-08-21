using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using OpenAI.Chat;
using Zava.SalesIntelligence.Agents;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;
using Zava.SalesIntelligence.Workflow;

// Phase 3: the squad as a workflow graph. Watcher retrieves, Thinker judges, Ranker
// orders in plain code, Operator prepares actions. Run it and stream the supersteps.
string dataverseUrl = RequireEnv("ZAVA_DATAVERSE_URL");
string openAiEndpoint = RequireEnv("ZAVA_OPENAI_ENDPOINT");
string fastDeployment = RequireEnv("ZAVA_OPENAI_DEPLOYMENT");
string reasoningDeployment = RequireEnv("ZAVA_OPENAI_REASONING_DEPLOYMENT");
var credential = new AzureCliCredential();

using var dataverse = DataverseClientFactory.Create(dataverseUrl, credential);
var queries = new WatcherQueries(dataverse);
var watcher = new WatcherService(queries);

var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential);
AIAgent thinker = openAiClient.GetChatClient(reasoningDeployment)
    .AsAIAgent(name: "Thinker", instructions: Thinker.BuildSystemPrompt());
AIAgent operatorAgent = openAiClient.GetChatClient(fastDeployment)
    .AsAIAgent(name: "Operator", instructions: Operator.BuildSystemPrompt());

var workflow = SalesWorkflow.Build(queries, watcher, thinker, operatorAgent);

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

        case WorkflowOutputEvent { Data: SalesBrief brief }:
            Console.WriteLine();
            foreach (var recommendation in brief.Recommendations)
            {
                var assessment = recommendation.Assessment;
                Console.WriteLine(
                    $"#{recommendation.Rank}  {assessment.AccountName}  " +
                    $"[{assessment.Archetype}]  bant={assessment.Bant.Score}  {assessment.Confidence}");
                foreach (var action in recommendation.Actions)
                {
                    Console.WriteLine($"     - {action.Type}: {action.Title}");
                }
            }
            break;
    }
}

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
