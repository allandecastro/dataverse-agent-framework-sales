using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Agents;
using Zava.SalesIntelligence.Dataverse;

namespace Zava.SalesIntelligence.Workflow;

// The seller's persona chooses the qualification lens over the same pipeline: SDR is the fast BANT
// triage; AE adds the deeper MEDDPICC read on top. Only the AE scorer differs - the graph, the
// identity boundary, and the deterministic BANT core are the same either way.
public enum Persona { Sdr, Ae }

// The built squad workflow together with the Dataverse clients it holds. The caller disposes
// Resources when the run is done - the ServiceClients are IDisposable and outlive Build().
public sealed record BuiltSquad(Microsoft.Agents.AI.Workflows.Workflow Workflow, IReadOnlyList<IDisposable> Resources);

// One place that wires the squad from the ZAVA_* configuration, shared by the console host and
// the companion so the identity split, the agent authorities, and the deployment choice live in a
// single reference implementation instead of being duplicated at each entry point.
public static class SquadFactory
{
    public static BuiltSquad Build(Persona? persona = null)
    {
        // The companion passes the toggle; the host falls back to ZAVA_PERSONA (default SDR).
        var resolved = persona ?? ResolvePersona();
        var dataverseUrl = Require("ZAVA_DATAVERSE_URL");
        var openAiEndpoint = Require("ZAVA_OPENAI_ENDPOINT");
        var reasoningDeployment = Require("ZAVA_OPENAI_REASONING_DEPLOYMENT");

        // Retrieval runs under the reader credential and writes under the writer credential, so
        // the same identity boundary holds at both entry points. Without the app-user secrets both
        // fall back to the interactive login (logged, so the boundary is auditable at startup).
        var credential = new AzureCliCredential();
        var readerCredential = AppUserCredential("reader", "ZAVA_READER_CLIENT_ID", "ZAVA_READER_CLIENT_SECRET", credential);
        var writerCredential = AppUserCredential("writer", "ZAVA_WRITER_CLIENT_ID", "ZAVA_WRITER_CLIENT_SECRET", credential);

        var readerClient = DataverseClientFactory.Create(dataverseUrl, readerCredential);
        var writerClient = DataverseClientFactory.Create(dataverseUrl, writerCredential);
        // Scope the account read to the seller by owner when running under the reader app identity
        // (which owns no accounts); falls back to the signed-in user when unset.
        Guid? sellerUserId = Guid.TryParse(Environment.GetEnvironmentVariable("ZAVA_SELLER_USER_ID"), out var seller) ? seller : null;
        var queries = new WatcherQueries(readerClient, sellerUserId);
        var watcher = new WatcherService(queries);
        var writer = new WriterTools(writerClient, sellerUserId);

        var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential);
        // WithAuthority wraps each agent in the identity middleware - it names the authority the
        // agent runs under on every call. Neither LLM agent holds a Dataverse credential. Both run
        // on the reasoning deployment (the cheaper model under-produced actions).
        AIAgent thinker = openAiClient.GetChatClient(reasoningDeployment)
            .AsAIAgent(name: "Thinker", instructions: Thinker.BuildSystemPrompt())
            .WithAuthority("reasoning only - no tools, no Dataverse identity");
        AIAgent operatorAgent = openAiClient.GetChatClient(reasoningDeployment)
            .AsAIAgent(name: "Operator", instructions: Operator.BuildSystemPrompt())
            .WithAuthority("reasoning only - proposes writes for the gate, holds no Dataverse identity");

        // AE persona only: the Scorer, same authority discipline as the Thinker (no tools,
        // no Dataverse identity). Null in SDR, so the run stays identical to the BANT-only path.
        Console.WriteLine($"[persona] {resolved}");
        AIAgent? meddpiccScorer = resolved == Persona.Ae
            ? openAiClient.GetChatClient(reasoningDeployment)
                .AsAIAgent(name: "Scorer", instructions: Meddpicc.BuildSystemPrompt())
                .WithAuthority("reasoning only - no tools, no Dataverse identity")
            : null;

        var pacing = TimeSpan.FromMilliseconds(
            int.TryParse(Environment.GetEnvironmentVariable("ZAVA_STEP_DELAY_MS"), out var ms) ? ms : 0);

        var workflow = SalesWorkflow.Build(queries, watcher, thinker, operatorAgent, writer, pacing, meddpiccScorer);
        return new BuiltSquad(workflow, [readerClient, writerClient]);
    }

    static Persona ResolvePersona() =>
        string.Equals(Environment.GetEnvironmentVariable("ZAVA_PERSONA"), "ae", StringComparison.OrdinalIgnoreCase)
            ? Persona.Ae
            : Persona.Sdr;

    static TokenCredential AppUserCredential(string label, string clientIdVar, string clientSecretVar, TokenCredential fallback)
    {
        var clientId = Environment.GetEnvironmentVariable(clientIdVar);
        var clientSecret = Environment.GetEnvironmentVariable(clientSecretVar);
        var tenantId = Environment.GetEnvironmentVariable("ZAVA_TENANT_ID");

        // Make the identity boundary auditable at startup: without the app-user secrets both the
        // reader and writer silently fall back to the interactive login, and the boundary is off.
        var isAppUser = clientId is not null && clientSecret is not null && tenantId is not null;
        Console.WriteLine(isAppUser
            ? $"[identity] {label}: app user {clientId}"
            : $"[identity] {label}: interactive az login (fallback - identity boundary NOT enforced)");

        return isAppUser
            ? new ClientSecretCredential(tenantId, clientId!, clientSecret!)
            : fallback;
    }

    static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
}
