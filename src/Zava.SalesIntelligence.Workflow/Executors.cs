using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Xrm.Sdk;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;

namespace Zava.SalesIntelligence.Workflow;

// Watcher: retrieves every account context. The per-account reads run concurrently
// inside the executor; the superstep barrier holds the Thinker until all are done.
[SendsMessage(typeof(ContextBatch))]
internal sealed partial class WatcherExecutor(WatcherQueries queries, WatcherService watcher, TimeSpan pacing)
    : Executor<string>("watcher")
{
    public override async ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        await context.AddEventAsync(new AgentProgressEvent("Watcher", "Querying Dataverse for the accounts owned by the seller"), cancellationToken);
        var accounts = await queries.GetAccountsAsync(cancellationToken);
        await context.AddEventAsync(new AgentProgressEvent("Watcher", $"{accounts.Count} accounts. Reading opportunities, activities, notes and contacts for each"), cancellationToken);
        var contexts = await Task.WhenAll(
            accounts.Select(account => BuildAndTraceAsync(account, context, cancellationToken)));
        await context.SendMessageAsync(new ContextBatch(contexts), cancellationToken);
    }

    async Task<AccountContext> BuildAndTraceAsync(AccountRecord account, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var accountContext = await watcher.BuildContextAsync(account, cancellationToken);
        await context.AddEventAsync(new AgentProgressEvent("Watcher",
            $"{accountContext.AccountName}: {accountContext.Opportunities.Count} open opp, {accountContext.Activities.Count} activities, {accountContext.Notes.Count} notes, {accountContext.Contacts.Count} contacts"),
            cancellationToken);
        return accountContext;
    }
}

// Thinker: judges each context against BANT. No tools; structured output only.
[SendsMessage(typeof(JudgedBatch))]
internal sealed partial class ThinkerExecutor(AIAgent thinker, TimeSpan pacing)
    : Executor<ContextBatch>("thinker")
{
    public override async ValueTask HandleAsync(ContextBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        await context.AddEventAsync(new AgentProgressEvent("Thinker", $"Judging {message.Contexts.Count} accounts against BANT - no tools, reasoning only"), cancellationToken);
        var judged = await Task.WhenAll(message.Contexts.Select(ctx => JudgeAndTraceAsync(ctx, context, cancellationToken)));
        await context.SendMessageAsync(new JudgedBatch(judged), cancellationToken);
    }

    async Task<Judged> JudgeAndTraceAsync(AccountContext accountContext, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var judged = await JudgeAsync(accountContext, cancellationToken);
        var a = judged.Assessment;
        // Show InsufficientSignal for a refusal rather than the archetype enum's default.
        var call = a.Confidence == Confidence.InsufficientSignal ? "InsufficientSignal" : a.Archetype.ToString();
        await context.AddEventAsync(new AgentThoughtEvent("Thinker",
            $"{a.AccountName} -> {call} (BANT {a.Bant.Score}/4): {Clip(a.Rationale)}"), cancellationToken);
        return judged;
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

    static string Clip(string text) => text.Length > 150 ? text[..147] + "..." : text;
}

// Ranker: plain code, deterministic. Drops refusals and orders the rest, keeping each
// assessment paired with its context.
[SendsMessage(typeof(RankedBatch))]
internal sealed partial class RankerExecutor(TimeSpan pacing) : Executor<JudgedBatch>("ranker")
{
    public override async ValueTask HandleAsync(JudgedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);

        var byAssessment = new Dictionary<OpportunityAssessment, Judged>(ReferenceEqualityComparer.Instance);
        foreach (var judged in message.Items)
        {
            byAssessment[judged.Assessment] = judged;
        }

        var assessed = message.Items.Select(judged => new AssessedOpportunity(judged.Assessment, judged.LastActivity));
        var ranked = Ranker.Rank(assessed, DateOnly.FromDateTime(DateTime.UtcNow))
            .Select(item => byAssessment[item.Assessment])
            .ToList();

        var dropped = message.Items
            .Where(judged => judged.Assessment.Confidence == Confidence.InsufficientSignal)
            .Select(judged => judged.Assessment.AccountName)
            .ToList();
        if (dropped.Count > 0)
        {
            await context.AddEventAsync(new AgentProgressEvent("Ranker", $"Dropped {dropped.Count} with insufficient signal: {string.Join(", ", dropped)}"), cancellationToken);
        }
        await context.AddEventAsync(new AgentProgressEvent("Ranker", $"Ordering {ranked.Count} opportunities by qualification - deterministic code, same every run"), cancellationToken);
        await context.AddEventAsync(new AgentProgressEvent("Ranker",
            "Order: " + string.Join("  ", ranked.Select((judged, index) => $"{index + 1}.{judged.Assessment.AccountName}"))), cancellationToken);
        await context.SendMessageAsync(new RankedBatch(ranked), cancellationToken);
    }
}

// Operator, first move: before preparing actions, it challenges the Thinker once if any
// ranked opportunity has an action that would rest on a Missing axis. Otherwise it hands
// straight to action preparation. This branch is the conditional challenge edge.
[SendsMessage(typeof(ChallengeBatch))]
[SendsMessage(typeof(ChallengedBatch))]
internal sealed partial class OperatorExecutor(TimeSpan pacing) : Executor<RankedBatch>("operator")
{
    public override async ValueTask HandleAsync(RankedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        if (message.Items.Any(judged => judged.HasMissingAxis))
        {
            var names = string.Join(", ", message.Items.Where(judged => judged.HasMissingAxis).Select(judged => judged.Assessment.AccountName));
            await context.AddEventAsync(new AgentProgressEvent("Operator", $"Missing BANT axes found - challenging the Thinker once on: {names}"), cancellationToken);
            await context.SendMessageAsync(new ChallengeBatch(message.Items), cancellationToken);
        }
        else
        {
            await context.AddEventAsync(new AgentProgressEvent("Operator", "No Missing axes to question - going straight to actions"), cancellationToken);
            await context.SendMessageAsync(new ChallengedBatch(message.Items), cancellationToken);
        }
    }
}

// Challenge: the Thinker re-examines the questioned assessments. Same agent, a challenge
// framing. Opportunities without a Missing axis pass through unchanged. One round trip.
[SendsMessage(typeof(ChallengedBatch))]
internal sealed partial class ChallengeExecutor(AIAgent thinker, TimeSpan pacing)
    : Executor<ChallengeBatch>("challenge")
{
    public override async ValueTask HandleAsync(ChallengeBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        var revised = await Task.WhenAll(message.Items.Select(judged => ReassessAsync(judged, context, cancellationToken)));
        await context.SendMessageAsync(new ChallengedBatch(revised), cancellationToken);
    }

    async Task<Judged> ReassessAsync(Judged judged, IWorkflowContext context, CancellationToken cancellationToken)
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
        await context.AddEventAsync(new AgentThoughtEvent("Thinker",
            $"Re-examined {assessment.AccountName}: {assessment.Archetype} (BANT {assessment.Bant.Score}/4)"), cancellationToken);
        return judged with { Assessment = assessment };
    }
}

// Operator, second move: turns each ranked assessment into up to three prepared actions,
// then sends them to the approval gate. Each action's target is set to the real
// opportunity id so an approved action can be written. It does not write itself.
[SendsMessage(typeof(ApprovalRequest))]
internal sealed partial class ActionExecutor(AIAgent operatorAgent, TimeSpan pacing)
    : Executor<ChallengedBatch>("actions")
{
    public override async ValueTask HandleAsync(ChallengedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        await context.AddEventAsync(new AgentProgressEvent("Operator", $"Preparing next best actions for {message.Items.Count} opportunities - three max each, tied to the evidence"), cancellationToken);
        var prepared = await Task.WhenAll(
            message.Items.Select((judged, index) => PrepareAndTraceAsync(judged, index + 1, context, cancellationToken)));

        var recommendations = new List<RankedRecommendation>();
        var proposals = new List<ActionProposal>();
        var number = 1;
        foreach (var (judged, recommendation) in message.Items.Zip(prepared))
        {
            recommendations.Add(recommendation);
            var opportunityId = judged.Context.Opportunities.Count > 0
                ? judged.Context.Opportunities[0].OpportunityId
                : Guid.Empty;
            foreach (var action in recommendation.Actions)
            {
                proposals.Add(new ActionProposal(
                    number++, recommendation.Assessment.OpportunityName, action with { TargetRecordId = opportunityId }));
            }
        }

        await context.AddEventAsync(new AgentProgressEvent("Operator", $"{proposals.Count} actions prepared - sending to the approval gate"), cancellationToken);
        await context.SendMessageAsync(new ApprovalRequest(recommendations, proposals), cancellationToken);
    }

    async Task<RankedRecommendation> PrepareAndTraceAsync(Judged judged, int rank, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var recommendation = await PrepareAsync(judged, rank, cancellationToken);
        var summary = recommendation.Actions.Count == 0
            ? "no action proposed"
            : string.Join("; ", recommendation.Actions.Select(action => $"{action.Type} - {action.Title}"));
        await context.AddEventAsync(new AgentThoughtEvent("Operator", $"{recommendation.Assessment.AccountName}: {summary}"), cancellationToken);
        return recommendation;
    }

    async Task<RankedRecommendation> PrepareAsync(Judged judged, int rank, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(judged.Assessment);
        var plan = await StructuredAgent.RunAsync<OperatorPlan>(operatorAgent, json, cancellationToken);
        // The model may return no actions at all; treat that as an empty plan.
        return new RankedRecommendation(rank, judged.Assessment, plan.Actions ?? []);
    }
}

// Execute: writes the approved actions. Creates (task, appointment) run under the current
// identity now; opportunity updates (stage, close date) mutate the fixture and land under
// the writer identity in phase 4, so they are approved here but applied there.
[YieldsOutput(typeof(WriteReport))]
internal sealed partial class ExecuteExecutor(WriterTools writer, TimeSpan pacing)
    : Executor<ApprovalDecision>("execute")
{
    public override async ValueTask HandleAsync(ApprovalDecision message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        if (message.Approved.Count > 0)
        {
            await context.AddEventAsync(new AgentProgressEvent("Operator", $"Writing {message.Approved.Count} approved action(s) to Dataverse under the writer identity"), cancellationToken);
        }

        var lines = new List<string>();
        foreach (var proposal in message.Approved)
        {
            lines.Add(await ExecuteAsync(proposal, cancellationToken));
        }
        if (message.Approved.Count == 0)
        {
            lines.Add("Nothing approved, nothing written.");
        }

        await context.YieldOutputAsync(new WriteReport(lines), cancellationToken);
    }

    async Task<string> ExecuteAsync(ActionProposal proposal, CancellationToken cancellationToken)
    {
        var action = proposal.Action;
        var regarding = new EntityReference("opportunity", action.TargetRecordId);

        switch (action.Type)
        {
            case ActionType.Task:
                await writer.CreateTaskAsync(regarding, action.Title, action.Body, action.DueDate, cancellationToken);
                return $"Wrote task '{action.Title}' on {proposal.OpportunityName}";

            case ActionType.Appointment:
                var start = action.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                await writer.CreateAppointmentAsync(regarding, action.Title, action.Body, start, cancellationToken);
                return $"Wrote appointment '{action.Title}' on {proposal.OpportunityName}";

            default:
                return $"Approved {action.Type} on {proposal.OpportunityName} (applied under the writer identity in phase 4)";
        }
    }
}
