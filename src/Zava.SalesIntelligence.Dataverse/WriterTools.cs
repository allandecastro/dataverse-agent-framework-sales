using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace Zava.SalesIntelligence.Dataverse;

// The Operator's four write tools, the only sanctioned writes. Fixed shapes, bounded
// parameters. Each maps to a permission on the Zava Agent Writer role: create on task
// and appointment, and write on opportunity limited to stage and estimated close date.
// No delete anywhere. These run only after approval - the gate is a workflow primitive,
// not a check in here.
public sealed class WriterTools(ServiceClient client, Guid? sellerUserId = null)
{
    static readonly Dictionary<string, int> Stages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Qualify"] = 0,
        ["Develop"] = 1,
        ["Propose"] = 2,
        ["Close"] = 3,
    };

    public Task<Guid> CreateTaskAsync(EntityReference regarding, string subject, string body, DateOnly? dueDate, CancellationToken cancellationToken)
    {
        var task = new Entity("task")
        {
            ["subject"] = subject,
            ["description"] = body,
            ["regardingobjectid"] = regarding,
        };
        if (dueDate is { } due)
        {
            task["scheduledend"] = Utc(due, TimeOnly.MinValue);
        }
        return client.CreateAsync(task, cancellationToken);
    }

    public Task<Guid> CreateAppointmentAsync(EntityReference regarding, string subject, string body, DateOnly start, Guid? clientContactId, CancellationToken cancellationToken)
    {
        var startTime = Utc(start, new TimeOnly(9, 0));
        var appointment = new Entity("appointment")
        {
            ["subject"] = subject,
            ["description"] = body,
            ["regardingobjectid"] = regarding,
            ["scheduledstart"] = startTime,
            ["scheduledend"] = startTime.AddHours(1),
        };

        // Make it a real meeting: the seller organises and is required, and the client contact is
        // invited. ownerid stays the writer app user so cleanup-demo-writes can find it. The demo
        // contacts carry no email address, so no invitation is actually sent to the client.
        if (sellerUserId is { } organiser)
        {
            appointment["organizer"] = Parties(Party("systemuser", organiser));
        }
        var required = new List<Entity>();
        if (sellerUserId is { } seller)
        {
            required.Add(Party("systemuser", seller));
        }
        if (clientContactId is { } contact)
        {
            required.Add(Party("contact", contact));
        }
        if (required.Count > 0)
        {
            appointment["requiredattendees"] = Parties([.. required]);
        }

        return client.CreateAsync(appointment, cancellationToken);
    }

    // An activityparty pointing at a systemuser or contact, and a party list to hang on an activity.
    static Entity Party(string entityLogicalName, Guid id) =>
        new("activityparty") { ["partyid"] = new EntityReference(entityLogicalName, id) };

    static EntityCollection Parties(params Entity[] parties) =>
        new(parties) { EntityName = "activityparty" };

    public Task UpdateOpportunityStageAsync(Guid opportunityId, string stage, CancellationToken cancellationToken)
    {
        if (!Stages.TryGetValue(stage, out var value))
        {
            throw new ArgumentException($"Unknown stage '{stage}'.", nameof(stage));
        }

        var opportunity = new Entity("opportunity", opportunityId)
        {
            ["salesstage"] = new OptionSetValue(value),
            ["stepname"] = stage,
        };
        return client.UpdateAsync(opportunity, cancellationToken);
    }

    public Task UpdateCloseDateAsync(Guid opportunityId, DateOnly closeDate, CancellationToken cancellationToken)
    {
        var opportunity = new Entity("opportunity", opportunityId)
        {
            ["estimatedclosedate"] = Utc(closeDate, TimeOnly.MinValue),
        };
        return client.UpdateAsync(opportunity, cancellationToken);
    }

    static DateTime Utc(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
}
