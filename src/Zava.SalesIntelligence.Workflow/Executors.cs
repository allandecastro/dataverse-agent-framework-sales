using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Xrm.Sdk;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;

namespace Zava.SalesIntelligence.Workflow;

// Watcher: retrieves the context for each account the seller owns. The per-account reads run
// concurrently inside the executor; the superstep barrier holds the Thinker until all are done.
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
        Judged judged;
        try
        {
            judged = await JudgeAsync(accountContext, cancellationToken);
        }
        catch (Exception ex)
        {
            // One account's model call failed (even after the retry). Drop that account
            // (InsufficientSignal) so the run continues, rather than faulting the whole stage
            // live. The Ranker drops InsufficientSignal anyway.
            await context.AddEventAsync(new AgentThoughtEvent("Thinker",
                $"{accountContext.AccountName} -> could not be judged this run ({ex.Message}); dropped"), cancellationToken);
            return new Judged(accountContext, InsufficientSignalFallback(accountContext), null);
        }

        var a = judged.Assessment;
        // Show InsufficientSignal for a refusal rather than the archetype enum's default.
        var call = a.Confidence == Confidence.InsufficientSignal ? "InsufficientSignal" : a.Archetype.ToString();
        await context.AddEventAsync(new AgentThoughtEvent("Thinker",
            $"{a.AccountName} -> {call} (BANT {a.Bant.Score}/4): {Clip(a.Rationale)}"), cancellationToken);
        return judged;
    }

    // A "not judged" assessment for an account whose model call failed, so the run degrades by
    // one account instead of crashing. InsufficientSignal makes the Ranker drop it.
    static OpportunityAssessment InsufficientSignalFallback(AccountContext accountContext)
    {
        var opportunity = accountContext.Opportunities.Count > 0 ? accountContext.Opportunities[0] : null;
        var missing = new BantCriterion(BantStatus.Missing, "Not judged this run after a model error.", []);
        return new OpportunityAssessment(
            accountContext.AccountName,
            opportunity?.Name ?? accountContext.AccountName,
            opportunity?.Amount ?? 0m,
            opportunity?.CloseDate,
            opportunity?.Stage ?? "",
            new BantAssessment(missing, missing, missing, missing),
            Archetype.Reconnect, // unused - InsufficientSignal governs and the Ranker drops it
            "This account could not be judged this run and was dropped.",
            Confidence.InsufficientSignal);
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

// Scorer (AE persona only): scores each account against MEDDPICC, no tools, reasoning only. It is
// a real fan-out branch - it consumes the same ContextBatch the Thinker does and runs concurrently
// with it. The fan-in barrier then holds both results until each branch has finished. In SDR there
// is no Scorer, so the graph stays the linear BANT path and the determinism gate is unaffected.
[SendsMessage(typeof(ScoredBatch))]
internal sealed partial class ScorerExecutor(AIAgent scorer, TimeSpan pacing)
    : Executor<ContextBatch>("scorer")
{
    public override async ValueTask HandleAsync(ContextBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        await context.AddEventAsync(new AgentProgressEvent("Scorer", $"Scoring {message.Contexts.Count} accounts against MEDDPICC - no tools, reasoning only"), cancellationToken);
        var scored = await Task.WhenAll(message.Contexts.Select(ctx => ScoreAndTraceAsync(ctx, context, cancellationToken)));
        await context.SendMessageAsync(new ScoredBatch(scored.OfType<Scored>().ToList()), cancellationToken);
    }

    async Task<Scored?> ScoreAndTraceAsync(AccountContext accountContext, IWorkflowContext context, CancellationToken cancellationToken)
    {
        try
        {
            var scorecard = await StructuredAgent.RunAsync<MeddpiccScorecard>(
                scorer, JsonSerializer.Serialize(accountContext), cancellationToken);
            await context.AddEventAsync(new AgentProgressEvent("Scorer",
                $"{accountContext.AccountName}: {scorecard.Score}/8 elements confirmed"), cancellationToken);
            // The scorer's call, like the Thinker's: the archetype it assigned, the eight statuses in
            // MEDDPICC order (C/P/M), and the rationale - so the log shows why.
            await context.AddEventAsync(new AgentThoughtEvent("Scorer",
                $"{accountContext.AccountName} -> {scorecard.Archetype} [{Statuses(scorecard)}]: {Clip(scorecard.Rationale)}"), cancellationToken);
            return new Scored(accountContext.AccountName, scorecard);
        }
        catch (Exception ex)
        {
            // One account's scorer call failed - skip its scorecard so the run continues and the
            // BANT judgment still stands, rather than faulting the whole branch.
            await context.AddEventAsync(new AgentThoughtEvent("Scorer",
                $"{accountContext.AccountName} -> not scored this run ({ex.Message})"), cancellationToken);
            return null;
        }
    }

    // "M:C EB:M DC:P DP:M PP:P IP:C Ch:P Co:M" - Metrics, Economic buyer, Decision criteria,
    // Decision process, Paper process, Identify pain, Champion, Competition; C/P/M for the status.
    static string Statuses(MeddpiccScorecard s) =>
        $"M:{s.Metrics.Status.ToString()[0]} EB:{s.EconomicBuyer.Status.ToString()[0]} DC:{s.DecisionCriteria.Status.ToString()[0]} "
        + $"DP:{s.DecisionProcess.Status.ToString()[0]} PP:{s.PaperProcess.Status.ToString()[0]} IP:{s.IdentifyPain.Status.ToString()[0]} "
        + $"Ch:{s.Champion.Status.ToString()[0]} Co:{s.Competition.Status.ToString()[0]}";

    static string Clip(string text) => text.Length > 200 ? text[..197] + "..." : text;
}

// Join (AE persona only): the fan-in barrier target. The barrier holds the Thinker's JudgedBatch
// and the Scorer's ScoredBatch until BOTH branches have produced a message, then streams them
// here. Join merges each scorecard onto its account's BANT judgment and hands the combined batch
// to the Ranker. It takes two input types, so it implements one IMessageHandler per type instead
// of deriving from the single-input Executor<T> the rest of the graph uses.
// ReflectingExecutor + IMessageHandler is the public multi-input executor API in Agent Framework
// 1.17 (the pinned version). It is marked obsolete in favour of a source generator that is not
// shipped in this package yet, so it stays the correct choice here; the suppression is scoped to
// this one class.
#pragma warning disable CS0618 // ReflectingExecutor/IMessageHandler: superseded by a generator not in 1.17
[SendsMessage(typeof(JudgedBatch))]
internal sealed partial class JoinExecutor(TimeSpan pacing)
    : ReflectingExecutor<JoinExecutor>("join"),
      IMessageHandler<JudgedBatch>,
      IMessageHandler<ScoredBatch>
{
    JudgedBatch? _judged;
    ScoredBatch? _scored;

    public async ValueTask HandleAsync(JudgedBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _judged = message;
        await EmitWhenBothArrivedAsync(context, cancellationToken);
    }

    public async ValueTask HandleAsync(ScoredBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _scored = message;
        await EmitWhenBothArrivedAsync(context, cancellationToken);
    }

    // The barrier delivers the two messages in the same superstep but as separate calls; emit only
    // once both have landed. The Scorer runs only in AE, so if it had nothing (an empty batch) the
    // judgments still pass through unchanged.
    async ValueTask EmitWhenBothArrivedAsync(IWorkflowContext context, CancellationToken cancellationToken)
    {
        if (_judged is null || _scored is null)
        {
            return;
        }

        await Task.Delay(pacing, cancellationToken);
        var byAccount = new Dictionary<string, MeddpiccScorecard>();
        foreach (var scored in _scored.Items)
        {
            byAccount[scored.AccountName] = scored.Scorecard;
        }

        var merged = _judged.Items
            .Select(judged => byAccount.TryGetValue(judged.Assessment.AccountName, out var scorecard)
                ? judged with { Meddpicc = scorecard }
                : judged)
            .ToList();

        await context.AddEventAsync(new AgentProgressEvent("Scorer",
            $"BANT and MEDDPICC joined for {merged.Count} accounts - both branches finished before the Ranker"), cancellationToken);
        await context.SendMessageAsync(new JudgedBatch(merged), cancellationToken);
    }
}
#pragma warning restore CS0618

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
        try
        {
            var assessment = await StructuredAgent.RunAsync<OpportunityAssessment>(thinker, input, cancellationToken);
            // A re-examination that degrades to InsufficientSignal is the model being inconsistent on
            // a thin account it already judged well enough to rank. Keep the original rather than let
            // one challenge round erase a prior confident judgment - which would otherwise drop the
            // account or, in SDR, surface it with a placeholder archetype.
            if (assessment.Confidence == Confidence.InsufficientSignal)
            {
                await context.AddEventAsync(new AgentThoughtEvent("Thinker",
                    $"Re-examination of {judged.Assessment.AccountName} was inconclusive; keeping the first assessment"), cancellationToken);
                return judged;
            }
            await context.AddEventAsync(new AgentThoughtEvent("Thinker",
                $"Re-examined {assessment.AccountName}: {assessment.Archetype} (BANT {assessment.Bant.Score}/4)"), cancellationToken);
            return judged with { Assessment = assessment };
        }
        catch (Exception ex)
        {
            // Re-examination failed - keep the first assessment rather than fault the run.
            await context.AddEventAsync(new AgentThoughtEvent("Thinker",
                $"Could not re-examine {judged.Assessment.AccountName} ({ex.Message}); keeping the first assessment"), cancellationToken);
            return judged;
        }
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
            // Resolve which opportunity the assessment (and its write) targets. With a single open
            // opportunity there is no ambiguity - use it directly rather than trust a model-
            // regenerated name. Only match by name when the account has more than one, keeping a
            // first-opportunity fallback so a write never lands nowhere.
            var opportunities = judged.Context.Opportunities;
            var opportunity = opportunities.Count switch
            {
                0 => null,
                1 => opportunities[0],
                _ => opportunities.FirstOrDefault(candidate => candidate.Name == recommendation.Assessment.OpportunityName)
                     ?? opportunities[0],
            };
            var opportunityId = opportunity?.OpportunityId ?? Guid.Empty;
            var contacts = judged.Context.Contacts;
            var primaryContact = contacts.FirstOrDefault(contact => contact.IsActive)
                ?? (contacts.Count > 0 ? contacts[0] : null);
            foreach (var action in recommendation.Actions)
            {
                // Invite the contact the action names (matched on first name) when we can find them,
                // else the account's primary active contact - a real client attendee either way.
                var named = contacts.FirstOrDefault(contact =>
                    FirstName(contact.FullName) is { Length: > 0 } first
                    && (action.Title.Contains(first, StringComparison.OrdinalIgnoreCase)
                        || action.Body.Contains(first, StringComparison.OrdinalIgnoreCase)));
                var contactId = (named ?? primaryContact)?.ContactId;
                proposals.Add(new ActionProposal(
                    number++, recommendation.Assessment.OpportunityName,
                    action with { TargetRecordId = opportunityId, TargetContactId = contactId }));
            }
        }

        await context.AddEventAsync(new AgentProgressEvent("Operator", $"{proposals.Count} actions prepared - sending to the approval gate"), cancellationToken);
        await context.SendMessageAsync(new ApprovalRequest(recommendations, proposals), cancellationToken);
    }

    async Task<RankedRecommendation> PrepareAndTraceAsync(Judged judged, int rank, IWorkflowContext context, CancellationToken cancellationToken)
    {
        RankedRecommendation recommendation;
        try
        {
            recommendation = await PrepareAsync(judged, rank, cancellationToken);
        }
        catch (Exception ex)
        {
            // Action prep for one account failed - propose nothing for it rather than fault the run.
            await context.AddEventAsync(new AgentThoughtEvent("Operator",
                $"{judged.Assessment.AccountName}: no actions this run ({ex.Message})"), cancellationToken);
            return new RankedRecommendation(rank, judged.Assessment, []);
        }

        var summary = recommendation.Actions.Count == 0
            ? "no action proposed"
            : string.Join("; ", recommendation.Actions.Select(action => $"{action.Type} - {action.Title}"));
        await context.AddEventAsync(new AgentThoughtEvent("Operator", $"{recommendation.Assessment.AccountName}: {summary}"), cancellationToken);

        // AE persona: the MEDDPICC scorecard was produced in parallel by the Scorer branch and
        // merged onto the judgment by the fan-in Join. Carry it onto the recommendation so the AE
        // board can read its archetype. Null in SDR, where there is no Scorer.
        return recommendation with { Meddpicc = judged.Meddpicc };
    }

    // Enums as their names, not integers, so the Operator reads "ReadyToClose" and "Confirmed"
    // rather than 0 and 2 in the assessment it reasons over.
    static readonly JsonSerializerOptions OperatorInput = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // The first token of a full name, for matching "Omar Said" against an action that says "Omar".
    static string FirstName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : "";
    }

    async Task<RankedRecommendation> PrepareAsync(Judged judged, int rank, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(judged.Assessment, OperatorInput);
        var plan = await StructuredAgent.RunAsync<OperatorPlan>(operatorAgent, json, cancellationToken);
        // The model may return no actions at all; treat that as an empty plan.
        return new RankedRecommendation(rank, judged.Assessment, plan.Actions ?? []);
    }
}

// Execute: writes the approved actions under the writer identity. All four tools write for real -
// creates (task, appointment) and opportunity updates (stage, close date). The stage and close
// date edits mutate the very fields the Watcher reads, so scripts/reset-demo.ps1 restores the
// opportunities to their dataset.json state before each run; that is what keeps the "same result
// every run" gate true even though the run now edits the source.
// The gate loops: on a partial approval the seller keeps the rest (message.Next), so this
// executor writes the approved batch and, if there is a next round, sends its ApprovalRequest
// back to the port instead of finishing. It accumulates every write across rounds and yields the
// final WriteReport only when the seller is done - so approving some actions never loses the rest.
[YieldsOutput(typeof(WriteReport))]
[SendsMessage(typeof(ApprovalRequest))]
internal sealed partial class ExecuteExecutor(WriterTools writer, TimeSpan pacing)
    : Executor<ApprovalDecision>("execute")
{
    readonly List<WriteResult> _written = [];

    public override async ValueTask HandleAsync(ApprovalDecision message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(pacing, cancellationToken);
        if (message.Approved.Count > 0)
        {
            await context.AddEventAsync(new AgentProgressEvent("Operator", $"Writing {message.Approved.Count} approved action(s) to Dataverse under the writer identity"), cancellationToken);
        }

        foreach (var proposal in message.Approved)
        {
            var result = await ExecuteAsync(proposal, cancellationToken);
            _written.Add(result);
            // Surface each write the moment it lands, so the companion can show the record and its
            // "Open record" link live - not only in the final report at the end of the loop.
            await context.AddEventAsync(new AgentWriteEvent(result), cancellationToken);
        }

        // Another round: re-open the gate with the proposals the seller kept.
        if (message.Next is { Proposals.Count: > 0 } next)
        {
            await context.AddEventAsync(new AgentProgressEvent("Operator", $"{next.Proposals.Count} action(s) kept for another round - back to the approval gate"), cancellationToken);
            await context.SendMessageAsync(next, cancellationToken);
            return;
        }

        // Done: report everything written across all rounds.
        if (_written.Count == 0)
        {
            _written.Add(new WriteResult("Nothing approved, nothing written."));
        }
        await context.YieldOutputAsync(new WriteReport(_written), cancellationToken);
    }

    async Task<WriteResult> ExecuteAsync(ActionProposal proposal, CancellationToken cancellationToken)
    {
        var action = proposal.Action;
        if (action.TargetRecordId == Guid.Empty)
        {
            return new WriteResult($"Skipped {action.Type} on {proposal.OpportunityName}: no target opportunity on the account");
        }
        var regarding = new EntityReference("opportunity", action.TargetRecordId);

        // A write that throws must not sink the whole run: catch it so the report still yields
        // and the failure is visible with its reason, instead of vanishing before YieldOutput.
        try
        {
            switch (action.Type)
            {
                case ActionType.Task:
                    var taskId = await writer.CreateTaskAsync(regarding, action.Title, action.Body, action.DueDate, cancellationToken);
                    return new WriteResult($"Wrote task '{action.Title}' on {proposal.OpportunityName}", "task", taskId);

                case ActionType.Appointment:
                    var start = action.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                    var appointmentId = await writer.CreateAppointmentAsync(regarding, action.Title, action.Body, start, action.TargetContactId, cancellationToken);
                    return new WriteResult($"Wrote appointment '{action.Title}' on {proposal.OpportunityName}", "appointment", appointmentId);

                case ActionType.StageUpdate:
                    // The model decides WHEN to correct the stage; code fixes WHAT to. A StageUpdate
                    // only ever walks an over-stated deal back to reality, so the bounded target is
                    // Qualify - no free-form stage from the model.
                    await writer.UpdateOpportunityStageAsync(action.TargetRecordId, "Qualify", cancellationToken);
                    return new WriteResult($"Wrote stage update on {proposal.OpportunityName} - moved to Qualify", "opportunity", action.TargetRecordId);

                case ActionType.CloseDateUpdate:
                    // The new date is the Operator's evidence-grounded suggestion (a bounded typed
                    // value, not a free-form query). No suggestion means nothing to write.
                    if (action.DueDate is not { } newCloseDate)
                    {
                        return new WriteResult($"Skipped close-date update on {proposal.OpportunityName}: no suggested date");
                    }
                    await writer.UpdateCloseDateAsync(action.TargetRecordId, newCloseDate, cancellationToken);
                    return new WriteResult($"Wrote close-date update on {proposal.OpportunityName} - moved to {newCloseDate.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}", "opportunity", action.TargetRecordId);

                default:
                    return new WriteResult($"Approved {action.Type} on {proposal.OpportunityName}");
            }
        }
        catch (Exception ex)
        {
            return new WriteResult($"Could not write {action.Type} on {proposal.OpportunityName}: {ex.Message}");
        }
    }
}
