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
    // meddpiccScorer is the AE persona's optional read: when supplied, the actions stage also
    // scores each opportunity against MEDDPICC. Null in the SDR persona - the graph is otherwise
    // identical, and archetypes/actions stay BANT-derived, so the determinism gate is unaffected.
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
        var actionExecutor = new ActionExecutor(operatorAgent, meddpiccScorer, pacing);
        var executeExecutor = new ExecuteExecutor(writer, pacing);

        // The approval gate is a RequestPort: the actions stage sends an ApprovalRequest,
        // the run pauses and emits it, and the approved actions route to the execute stage.
        // Nothing is written without a decision.
        var approvalPort = RequestPort.Create<ApprovalRequest, ApprovalDecision>("approval");

        // The challenge edge is conditional by message TYPE, not a predicate: the Operator emits
        // either a ChallengeBatch (route to the Challenge, Thinker re-examines) or a ChallengedBatch
        // (straight to action prep), and the two edges out of the Operator fire by which type it
        // sent. Both paths converge on the actions, so the round trip is capped at one.
        return new WorkflowBuilder(watcherExecutor)
            .AddEdge(watcherExecutor, thinkerExecutor)
            .AddEdge(thinkerExecutor, rankerExecutor)
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
