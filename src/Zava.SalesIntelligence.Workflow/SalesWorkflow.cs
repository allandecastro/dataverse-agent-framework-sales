using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Zava.SalesIntelligence.Dataverse;

namespace Zava.SalesIntelligence.Workflow;

// The squad as a graph: Watcher retrieves, the Thinker judges, the Ranker orders in
// plain code, the Operator prepares actions. Each arrow is a superstep barrier - the
// next stage starts only when the previous one has finished for every account.
public static class SalesWorkflow
{
    // pacing is a server-side delay in front of each stage, so the companion can show
    // the supersteps arriving at a readable rhythm. Zero for a full-speed run.
    public static Microsoft.Agents.AI.Workflows.Workflow Build(
        WatcherQueries queries,
        WatcherService watcher,
        AIAgent thinker,
        AIAgent operatorAgent,
        TimeSpan pacing)
    {
        var watcherExecutor = new WatcherExecutor(queries, watcher, pacing);
        var thinkerExecutor = new ThinkerExecutor(thinker, pacing);
        var rankerExecutor = new RankerExecutor(pacing);
        var operatorExecutor = new OperatorExecutor(pacing);
        var challengeExecutor = new ChallengeExecutor(thinker, pacing);
        var actionExecutor = new ActionExecutor(operatorAgent, pacing);

        // The challenge edge is conditional: the Operator either routes to the Challenge
        // (Thinker re-examines) or straight to action preparation. Either way the flow
        // converges on the actions, so the round trip is capped at one.
        return new WorkflowBuilder(watcherExecutor)
            .AddEdge(watcherExecutor, thinkerExecutor)
            .AddEdge(thinkerExecutor, rankerExecutor)
            .AddEdge(rankerExecutor, operatorExecutor)
            .AddEdge(operatorExecutor, challengeExecutor)
            .AddEdge(operatorExecutor, actionExecutor)
            .AddEdge(challengeExecutor, actionExecutor)
            .WithOutputFrom(actionExecutor)
            .Build();
    }
}
