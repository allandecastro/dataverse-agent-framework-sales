using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace Zava.SalesIntelligence.Dataverse;

// The Operator's four write tools, the only sanctioned writes. Fixed shapes, bounded
// parameters. Each maps to a permission on the Zava Agent Writer role: create on task
// and appointment, and write on opportunity limited to stage and estimated close date.
// No delete anywhere. These run only after approval - the gate is a workflow primitive,
// not a check in here.
public sealed class WriterTools(ServiceClient client)
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

    public Task<Guid> CreateAppointmentAsync(EntityReference regarding, string subject, string body, DateOnly start, CancellationToken cancellationToken)
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
        return client.CreateAsync(appointment, cancellationToken);
    }

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
