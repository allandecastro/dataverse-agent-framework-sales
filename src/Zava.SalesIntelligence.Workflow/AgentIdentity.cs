using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Zava.SalesIntelligence.Workflow;

// A MAF agent run-middleware that makes each agent's authority legible. It wraps the agent with
// AIAgentBuilder.Use, so every run announces the identity the agent acts under before delegating.
//
// Honest scope: the squad's LLM agents (Thinker, Operator) carry NO Dataverse credential - they
// reason over data the Watcher already read, and the Operator only proposes writes for the
// approval gate. The real identity boundary lives at the Dataverse client layer (the reader and
// writer application users, enforced by Dataverse roles). This middleware does not add security;
// it makes the model visible - "the Thinker has no tools and no identity" is stamped on every run,
// not just asserted in a comment. It is the MAF middleware primitive applied to a cross-cutting
// concern, exactly where the framework intends it.
public static class AgentIdentity
{
    public static AIAgent WithAuthority(this AIAgent agent, string authority)
    {
        var name = agent.Name;
        return agent.AsBuilder()
            .Use(async (messages, session, options, next, cancellationToken) =>
            {
                Console.WriteLine($"[identity] {name} running under authority: {authority}");
                await next(messages, session, options, cancellationToken);
            })
            .Build();
    }
}
