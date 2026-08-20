using Microsoft.Extensions.AI;

namespace Zava.SalesIntelligence.Dataverse.Tools;

// The Watcher's account-listing tool, exposed to the agent. It wraps the fixed
// query in WatcherQueries so there is a single source of truth for the retrieval.
public sealed class AccountTools(WatcherQueries queries)
{
    public AIFunction GetAccountsFunction() =>
        AIFunctionFactory.Create(
            queries.GetAccountsAsync,
            name: "get_accounts",
            description: "Get the accounts owned by the current seller. Returns each account name and number.");
}
