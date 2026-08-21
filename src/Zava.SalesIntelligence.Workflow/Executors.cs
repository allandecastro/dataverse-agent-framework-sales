using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;

namespace Zava.SalesIntelligence.Workflow;

// Watcher: retrieves every account context. The per-account reads run concurrently
// inside the executor; the superstep barrier holds the Thinker until all are done.
[SendsMessage(typeof(ContextBatch))]
internal sealed partial class WatcherExecutor(WatcherQueries queries, WatcherService watcher)
    : Executor<string>("watcher")
{
    public override async ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var accounts = await queries.GetAccountsAsync(cancellationToken);
        var contexts = await Task.WhenAll(
            accounts.Select(account => watcher.BuildContextAsync(account, cancellationToken)));
        await context.SendMessageAsync(new ContextBatch(contexts), cancellationToken);
    }
}

// Thinker: judges each context against BANT. No tools; structured output only.
[SendsMessage(typeof(AssessmentBatch))]
internal sealed partial class ThinkerExecutor(AIAgent thinker)
    : Executor<ContextBatch>("thinker")
{
    public override async ValueTask HandleAsync(ContextBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var assessed = await Task.WhenAll(message.Contexts.Select(ctx => AssessAsync(ctx, cancellationToken)));
        await context.SendMessageAsync(new AssessmentBatch(assessed), cancellationToken);
    }

    async Task<AssessedOpportunity> AssessAsync(AccountContext accountContext, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(accountContext);
        var assessment = await StructuredAgent.RunAsync<OpportunityAssessment>(thinker, json, cancellationToken);
        var lastActivity = accountContext.Activities.Count > 0
            ? accountContext.Activities.Max(activity => activity.Date)
            : (DateOnly?)null;
        return new AssessedOpportunity(assessment, lastActivity);
    }
}

// Ranker: plain code, deterministic. Drops refusals and orders the rest.
[SendsMessage(typeof(RankedBatch))]
internal sealed partial class RankerExecutor() : Executor<AssessmentBatch>("ranker")
{
    public override async ValueTask HandleAsync(AssessmentBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var ranked = Ranker.Rank(message.Assessments, DateOnly.FromDateTime(DateTime.UtcNow));
        await context.SendMessageAsync(new RankedBatch(ranked), cancellationToken);
    }
}

// Operator: turns each ranked assessment into prepared actions and yields the brief.
[YieldsOutput(typeof(SalesBrief))]
internal sealed partial class OperatorExecutor(AIAgent operatorAgent)
    : Executor<RankedBatch>("operator")
{
    public override async ValueTask HandleAsync(RankedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var recommendations = await Task.WhenAll(
            message.Ranked.Select((item, index) => PrepareAsync(item, index + 1, cancellationToken)));
        await context.YieldOutputAsync(new SalesBrief(recommendations), cancellationToken);
    }

    async Task<RankedRecommendation> PrepareAsync(AssessedOpportunity item, int rank, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(item.Assessment);
        var plan = await StructuredAgent.RunAsync<OperatorPlan>(operatorAgent, json, cancellationToken);
        // The model may return no actions at all; treat that as an empty plan.
        return new RankedRecommendation(rank, item.Assessment, plan.Actions ?? []);
    }
}
