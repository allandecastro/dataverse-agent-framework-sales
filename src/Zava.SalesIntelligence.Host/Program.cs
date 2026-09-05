using Microsoft.Agents.AI.Workflows;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Workflow;

// Phase 3 and 4: the squad as a workflow graph, with an approval gate. Watcher retrieves,
// Thinker judges, Ranker orders, Operator prepares actions, the run pauses for approval,
// and approved actions are written. Stream the supersteps.
// Build the squad from the ZAVA_* configuration: the reader/writer identity split, the agents
// wrapped in the identity middleware, the deployment choice. Shared with the companion via
// SquadFactory so the wiring lives in one reference implementation. Set the app-user secrets to
// make the identity boundary real; until then both fall back to the interactive login (logged).
var built = SquadFactory.Build();
var workflow = built.Workflow;

// `dotnet run ... graph` prints the workflow graph as Mermaid and exits, without running it.
if (args.Contains("graph"))
{
    Console.WriteLine(workflow.ToMermaidString());
    foreach (var resource in built.Resources) { resource.Dispose(); }
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
                Console.WriteLine($"  {line.Message}");
            }
            break;
    }
}

// Dispose the Dataverse ServiceClients the build created.
foreach (var resource in built.Resources) { resource.Dispose(); }

// The seller's decision. Interactive prompting is a companion concern; here the policy
// comes from ZAVA_APPROVE: "all", "none" (default), or a comma-separated list of numbers.
static IReadOnlyList<ActionProposal> Approve(ApprovalRequest request)
{
    Console.WriteLine();
    Console.WriteLine("Approval needed. Ranked focus:");
    foreach (var recommendation in request.Recommendations)
    {
        var assessment = recommendation.Assessment;
        // AE persona: the MEDDPICC scorecard drives the board, so show its archetype and score.
        var line = recommendation.Meddpicc is { } scorecard
            ? $"[{scorecard.Archetype}]  meddpicc={scorecard.Score}/8"
            : $"[{assessment.Archetype}]  bant={assessment.Bant.Score}/4";
        Console.WriteLine($"  #{recommendation.Rank}  {assessment.AccountName}  {line}");
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
