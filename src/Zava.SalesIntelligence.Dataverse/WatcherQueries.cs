using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Dataverse;

// The Watcher's five reads. Every query is fixed here with bounded parameters.
// The model chooses when to call a tool, never what to ask. Record ids are always
// preserved so the Thinker can cite evidence that exists.
//
// sellerUserId scopes the pipeline to one seller's accounts. When the agent runs under
// its own scoped app identity (reader/writer), that identity owns no accounts, so the
// query targets the seller by owner rather than EqualUserId. The agent keeps its own
// least-privilege identity - this is not impersonation, so there is no privilege
// escalation. When null (interactive run), it falls back to the signed-in user.
public sealed class WatcherQueries(ServiceClient client, Guid? sellerUserId = null)
{
    const int MaxAccounts = 25;
    const int MaxActivityWindowDays = 180;

    public async Task<IReadOnlyList<AccountRecord>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var query = new QueryExpression("account")
        {
            ColumnSet = new ColumnSet("accountid", "name", "accountnumber"),
            TopCount = MaxAccounts,
            Criteria = new FilterExpression(),
        };
        if (sellerUserId is { } seller)
        {
            query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, seller);
        }
        else
        {
            query.Criteria.AddCondition("ownerid", ConditionOperator.EqualUserId);
        }

        var result = await client.RetrieveMultipleAsync(query, cancellationToken);
        return result.Entities.Select(e => new AccountRecord(
            e.Id,
            e.GetAttributeValue<string>("name"),
            e.GetAttributeValue<string>("accountnumber"))).ToList();
    }

    public async Task<IReadOnlyList<OpportunityRecord>> GetOpenOpportunitiesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("opportunity")
        {
            ColumnSet = new ColumnSet("opportunityid", "name", "estimatedvalue", "estimatedclosedate", "stepname"),
            Criteria = new FilterExpression(),
        };
        query.Criteria.AddCondition("customerid", ConditionOperator.Equal, accountId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // open only

        var result = await client.RetrieveMultipleAsync(query, cancellationToken);
        return result.Entities.Select(e => new OpportunityRecord(
            e.Id,
            e.GetAttributeValue<string>("name"),
            e.GetAttributeValue<Money>("estimatedvalue")?.Value,
            e.GetAttributeValue<string>("stepname"),
            AsDateOnly(e, "estimatedclosedate"))).ToList();
    }

    public async Task<IReadOnlyList<ActivityRecord>> GetActivitiesAsync(Guid accountId, int sinceDays, CancellationToken cancellationToken)
    {
        var window = Math.Clamp(sinceDays, 1, MaxActivityWindowDays);
        var cutoff = DateTime.UtcNow.AddDays(-window);

        var query = new QueryExpression("activitypointer")
        {
            ColumnSet = new ColumnSet("activityid", "activitytypecode", "subject", "scheduledstart", "description"),
            Criteria = new FilterExpression(),
        };
        query.Criteria.AddCondition("regardingobjectid", ConditionOperator.Equal, accountId);
        query.Criteria.AddCondition("scheduledstart", ConditionOperator.OnOrAfter, cutoff);

        var result = await client.RetrieveMultipleAsync(query, cancellationToken);
        return result.Entities.Select(e => new ActivityRecord(
            e.Id,
            e.GetAttributeValue<string>("activitytypecode") ?? "activity",
            e.GetAttributeValue<string>("subject"),
            AsDateOnly(e, "scheduledstart") ?? default,
            e.GetAttributeValue<string>("description"))).ToList();
    }

    public async Task<IReadOnlyList<NoteRecord>> GetNotesAsync(Guid regardingId, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("annotation")
        {
            ColumnSet = new ColumnSet("annotationid", "subject", "notetext", "createdon", "objecttypecode"),
            Criteria = new FilterExpression(),
        };
        query.Criteria.AddCondition("objectid", ConditionOperator.Equal, regardingId);

        var result = await client.RetrieveMultipleAsync(query, cancellationToken);
        return result.Entities.Select(e => new NoteRecord(
            e.Id,
            e.GetAttributeValue<string>("subject"),
            e.GetAttributeValue<string>("notetext"),
            AsDateOnly(e, "createdon") ?? default,
            regardingId,
            e.GetAttributeValue<string>("objecttypecode") ?? string.Empty)).ToList();
    }

    public async Task<IReadOnlyList<ContactRecord>> GetContactsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "fullname", "jobtitle", "statecode"),
            Criteria = new FilterExpression(),
        };
        query.Criteria.AddCondition("parentcustomerid", ConditionOperator.Equal, accountId);

        var result = await client.RetrieveMultipleAsync(query, cancellationToken);
        return result.Entities.Select(e => new ContactRecord(
            e.Id,
            e.GetAttributeValue<string>("fullname"),
            e.GetAttributeValue<string>("jobtitle"),
            e.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0)).ToList();
    }

    static DateOnly? AsDateOnly(Entity entity, string attribute) =>
        entity.TryGetAttributeValue<DateTime>(attribute, out var value)
            ? DateOnly.FromDateTime(value)
            : null;
}
