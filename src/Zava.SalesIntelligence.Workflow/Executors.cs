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
[SendsMessage(typeof(JudgedBatch))]
internal sealed partial class ThinkerExecutor(AIAgent thinker)
    : Executor<ContextBatch>("thinker")
{
    public override async ValueTask HandleAsync(ContextBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var judged = await Task.WhenAll(message.Contexts.Select(ctx => JudgeAsync(ctx, cancellationToken)));
        await context.SendMessageAsync(new JudgedBatch(judged), cancellationToken);
    }

    async Task<Judged> JudgeAsync(AccountContext accountContext, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(accountContext);
        var assessment = await StructuredAgent.RunAsync<OpportunityAssessment>(thinker, json, cancellationToken);
        var lastActivity = accountContext.Activities.Count > 0
            ? accountContext.Activities.Max(activity => activity.Date)
            : (DateOnly?)null;
        return new Judged(accountContext, assessment, lastActivity);
    }
}

// Ranker: plain code, deterministic. Drops refusals and orders the rest, keeping each
// assessment paired with its context.
[SendsMessage(typeof(RankedBatch))]
internal sealed partial class RankerExecutor() : Executor<JudgedBatch>("ranker")
{
    public override async ValueTask HandleAsync(JudgedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var byAssessment = new Dictionary<OpportunityAssessment, Judged>(ReferenceEqualityComparer.Instance);
        foreach (var judged in message.Items)
        {
            byAssessment[judged.Assessment] = judged;
        }

        var assessed = message.Items.Select(judged => new AssessedOpportunity(judged.Assessment, judged.LastActivity));
        var ranked = Ranker.Rank(assessed, DateOnly.FromDateTime(DateTime.UtcNow))
            .Select(item => byAssessment[item.Assessment])
            .ToList();

        await context.SendMessageAsync(new RankedBatch(ranked), cancellationToken);
    }
}

// Operator, first move: before preparing actions, it challenges the Thinker once if any
// ranked opportunity has an action that would rest on a Missing axis. Otherwise it hands
// straight to action preparation. This branch is the conditional challenge edge.
[SendsMessage(typeof(ChallengeBatch))]
[SendsMessage(typeof(ChallengedBatch))]
internal sealed partial class OperatorExecutor() : Executor<RankedBatch>("operator")
{
    public override async ValueTask HandleAsync(RankedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (message.Items.Any(judged => judged.HasMissingAxis))
        {
            await context.SendMessageAsync(new ChallengeBatch(message.Items), cancellationToken);
        }
        else
        {
            await context.SendMessageAsync(new ChallengedBatch(message.Items), cancellationToken);
        }
    }
}

// Challenge: the Thinker re-examines the questioned assessments. Same agent, a challenge
// framing. Opportunities without a Missing axis pass through unchanged. One round trip.
[SendsMessage(typeof(ChallengedBatch))]
internal sealed partial class ChallengeExecutor(AIAgent thinker)
    : Executor<ChallengeBatch>("challenge")
{
    public override async ValueTask HandleAsync(ChallengeBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var revised = await Task.WhenAll(message.Items.Select(judged => ReassessAsync(judged, cancellationToken)));
        await context.SendMessageAsync(new ChallengedBatch(revised), cancellationToken);
    }

    async Task<Judged> ReassessAsync(Judged judged, CancellationToken cancellationToken)
    {
        if (!judged.HasMissingAxis)
        {
            return judged;
        }

        var input = $$"""
            The Operator is challenging your assessment: you marked one or more BANT axes
            Missing. Re-examine the evidence below and return your assessment again. For each
            Missing axis, confirm it by noting what is absent, or revise it only if the
            evidence supports a change.

            {{JsonSerializer.Serialize(judged.Context)}}
            """;
        var assessment = await StructuredAgent.RunAsync<OpportunityAssessment>(thinker, input, cancellationToken);
        return judged with { Assessment = assessment };
    }
}

// Operator, second move: turns each ranked assessment into up to three prepared actions
// and yields the brief.
[YieldsOutput(typeof(SalesBrief))]
internal sealed partial class ActionExecutor(AIAgent operatorAgent)
    : Executor<ChallengedBatch>("actions")
{
    public override async ValueTask HandleAsync(ChallengedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var recommendations = await Task.WhenAll(
            message.Items.Select((judged, index) => PrepareAsync(judged, index + 1, cancellationToken)));
        await context.YieldOutputAsync(new SalesBrief(recommendations), cancellationToken);
    }

    async Task<RankedRecommendation> PrepareAsync(Judged judged, int rank, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(judged.Assessment);
        var plan = await StructuredAgent.RunAsync<OperatorPlan>(operatorAgent, json, cancellationToken);
        // The model may return no actions at all; treat that as an empty plan.
        return new RankedRecommendation(rank, judged.Assessment, plan.Actions ?? []);
    }
}
