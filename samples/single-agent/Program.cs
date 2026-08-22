using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Dataverse;
using Zava.SalesIntelligence.Dataverse.Tools;

// The comparison, act 0. One agent holds every read tool and answers the same question the
// squad does - but with no workflow graph, no superstep barrier, no deterministic Ranker,
// no approval gate, and no identity separation. Put this next to the squad to see what the
// harness actually buys you: order, determinism, and a place to stand for governance.
//
// It stays read-only on purpose. The point of the squad is not that one agent cannot read
// Dataverse - it can. The point is that once you add writes, ranking, and an identity
// boundary, a single ungated agent is the wrong place to put them.

string dataverseUrl = Require("ZAVA_DATAVERSE_URL");
string openAiEndpoint = Require("ZAVA_OPENAI_ENDPOINT");
string deployment = Require("ZAVA_OPENAI_DEPLOYMENT");

var credential = new AzureCliCredential();
using var client = DataverseClientFactory.Create(dataverseUrl, credential);

// Same fixed queries as the Watcher. Scope to the seller when set, else the signed-in user.
Guid? sellerUserId = Guid.TryParse(Environment.GetEnvironmentVariable("ZAVA_SELLER_USER_ID"), out var seller) ? seller : null;
var queries = new WatcherQueries(client, sellerUserId);
var accountTools = new AccountTools(queries);

// Every read the Watcher has, handed to one agent as tools. The model decides when to call
// them and in what order - there is no graph telling it to fan out per account first.
AIFunction getOpportunities = AIFunctionFactory.Create(
    queries.GetOpenOpportunitiesAsync,
    name: "get_open_opportunities",
    description: "Get the open opportunities for one account. Pass the account id from get_accounts.");
AIFunction getActivities = AIFunctionFactory.Create(
    (Guid accountId, CancellationToken ct) => queries.GetActivitiesAsync(accountId, 90, ct),
    name: "get_recent_activities",
    description: "Get the recent activities for one account. Pass the account id from get_accounts.");

var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential);
AIAgent agent = openAiClient.GetChatClient(deployment).AsAIAgent(
    name: "Sales assistant",
    instructions:
        """
        You are a sales assistant. The seller wants to know what to focus on this week.
        Read their pipeline with the tools, then recommend where to spend time and why.
        Ground every recommendation in a record you retrieved. Be concise.
        """,
    tools: [accountTools.GetAccountsFunction(), getOpportunities, getActivities]);

Console.WriteLine("Asking one agent, with all the read tools...");
Console.WriteLine();
var response = await agent.RunAsync("What should I focus on this week?");
Console.WriteLine(response.Text);

static string Require(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
