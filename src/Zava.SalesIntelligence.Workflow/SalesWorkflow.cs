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
        var operatorExecutor = new OperatorExecutor(operatorAgent);

        return new WorkflowBuilder(watcherExecutor)
            .AddEdge(watcherExecutor, thinkerExecutor)
            .AddEdge(thinkerExecutor, rankerExecutor)
            .AddEdge(rankerExecutor, operatorExecutor)
            .WithOutputFrom(operatorExecutor)
            .Build();
    }
}
