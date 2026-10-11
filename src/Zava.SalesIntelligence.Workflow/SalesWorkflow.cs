using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;

namespace Zava.SalesIntelligence.Workflow;

// The squad as a graph: Watcher retrieves, the Thinker judges, the Ranker orders in
// plain code, the Operator prepares actions. Each arrow is a superstep barrier - the
// next stage starts only when the previous one has finished for every owned account.
public static class SalesWorkflow
{
    // pacing is a server-side delay in front of each stage, so the companion can show
    // the supersteps arriving at a readable rhythm. Zero for a full-speed run.
    // meddpiccScorer is the AE persona's extra read: when supplied, the actions stage also scores
    // each opportunity against MEDDPICC, and that scorecard's archetype drives the AE board. Null
    // in the SDR persona - the graph is identical, and archetypes stay BANT-derived there, so the
    // SDR determinism gate is unaffected.
    public static Microsoft.Agents.AI.Workflows.Workflow Build(
        WatcherQueries queries,
        WatcherService watcher,
        AIAgent thinker,
        AIAgent operatorAgent,
        WriterTools writer,
        TimeSpan pacing,
        AIAgent? meddpiccScorer = null)
    {
        var watcherExecutor = new WatcherExecutor(queries, watcher, pacing);
        var thinkerExecutor = new ThinkerExecutor(thinker, pacing);
        var rankerExecutor = new RankerExecutor(pacing);
        var operatorExecutor = new OperatorExecutor(pacing);
        var challengeExecutor = new ChallengeExecutor(thinker, pacing);
        var actionExecutor = new ActionExecutor(operatorAgent, pacing);
        var executeExecutor = new ExecuteExecutor(writer, pacing);

        // The approval gate is a RequestPort: the actions stage sends an ApprovalRequest,
        // the run pauses and emits it, and the approved actions route to the execute stage.
        // Nothing is written without a decision.
        var approvalPort = RequestPort.Create<ApprovalRequest, ApprovalDecision>("approval");

        // The head of the graph depends on the persona. AE adds a MEDDPICC Scorer that runs in
        // parallel with the Thinker: Watcher fans out to both, and a fan-in barrier holds the
        // Ranker until both branches finish, with a Join merging the two reads. SDR has no Scorer,
        // so the Thinker feeds the Ranker directly - a single linear head. The tail (Ranker ->
        // Operator -> challenge/actions -> approval gate -> execute, with the approval loop) is the
        // same for both personas.
        var builder = new WorkflowBuilder(watcherExecutor);
        if (meddpiccScorer is not null)
        {
            var scorerExecutor = new ScorerExecutor(meddpiccScorer, pacing);
            var joinExecutor = new JoinExecutor(pacing);
            builder
                // Real fan-out: the same ContextBatch goes to both the Thinker (BANT) and the
                // Scorer (MEDDPICC), which run concurrently.
                .AddFanOutEdge(watcherExecutor, [thinkerExecutor, scorerExecutor])
                // Real fan-in barrier: the Join runs once both branches have produced a result.
                .AddFanInBarrierEdge([thinkerExecutor, scorerExecutor], joinExecutor)
                .AddEdge(joinExecutor, rankerExecutor);
        }
        else
        {
            builder.AddEdge(watcherExecutor, thinkerExecutor)
                .AddEdge(thinkerExecutor, rankerExecutor);
        }

        // The challenge edge is conditional by message TYPE, not a predicate: the Operator emits
        // either a ChallengeBatch (route to the Challenge, Thinker re-examines) or a ChallengedBatch
        // (straight to action prep), and the two edges out of the Operator fire by which type it
        // sent. Both paths converge on the actions, so the round trip is capped at one.
        return builder
            .AddEdge(rankerExecutor, operatorExecutor)
            .AddEdge(operatorExecutor, challengeExecutor)
            .AddEdge(operatorExecutor, actionExecutor)
            .AddEdge(challengeExecutor, actionExecutor)
            .AddEdge(actionExecutor, approvalPort)
            .AddEdge(approvalPort, executeExecutor)
            // The iterative-gate loop: a partial approval sends the remaining proposals back to
            // the port for another round, so the seller can approve in batches without losing the rest.
            .AddEdge(executeExecutor, approvalPort)
            .WithOutputFrom(executeExecutor)
            .Build();
    }
}
