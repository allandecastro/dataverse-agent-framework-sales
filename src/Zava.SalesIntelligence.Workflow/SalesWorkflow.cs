using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Zava.SalesIntelligence.Dataverse;

namespace Zava.SalesIntelligence.Workflow;

// The squad as a graph: Watcher retrieves, the Thinker judges, the Ranker orders in
// plain code, the Operator prepares actions. Each arrow is a superstep barrier - the
// next stage starts only when the previous one has finished for every account.
public static class SalesWorkflow
{
    public static Microsoft.Agents.AI.Workflows.Workflow Build(
        WatcherQueries queries,
        WatcherService watcher,
        AIAgent thinker,
        AIAgent operatorAgent)
    {
        var watcherExecutor = new WatcherExecutor(queries, watcher);
        var thinkerExecutor = new ThinkerExecutor(thinker);
        var rankerExecutor = new RankerExecutor();
        var operatorExecutor = new OperatorExecutor();
        var challengeExecutor = new ChallengeExecutor(thinker);
        var actionExecutor = new ActionExecutor(operatorAgent);

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
