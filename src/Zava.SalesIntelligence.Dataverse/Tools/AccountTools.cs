using Microsoft.Extensions.AI;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;

namespace Zava.SalesIntelligence.Dataverse.Tools;

public sealed record AccountSummary(string Name, string? AccountNumber);

// The seller's account retrieval. The query is fixed in code with bounded results.
// The model decides when to call this, never what to ask.
public sealed class AccountTools(ServiceClient client)
{
    const int MaxAccounts = 25;

    public AIFunction GetAccountsFunction() =>
        AIFunctionFactory.Create(
            GetAccountsAsync,
            name: "get_accounts",
            description: "Get the accounts owned by the current seller. Returns each account name and number.");

    async Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var query = new QueryExpression("account")
        {
            ColumnSet = new ColumnSet("name", "accountnumber"),
            TopCount = MaxAccounts,
            Criteria = new FilterExpression(),
        };
        query.Criteria.AddCondition("ownerid", ConditionOperator.EqualUserId);

        var result = await client.RetrieveMultipleAsync(query, cancellationToken);
        return result.Entities
            .Select(e => new AccountSummary(
                e.GetAttributeValue<string>("name"),
                e.GetAttributeValue<string>("accountnumber")))
            .ToList();
    }
}
