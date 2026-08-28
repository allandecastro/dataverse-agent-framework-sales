namespace Zava.SalesIntelligence.Agents.Contracts;

public record PreparedAction(
    ActionType Type,            // Task, Appointment, StageUpdate, CloseDateUpdate
    string AccountName,
    Guid TargetRecordId,
    string Title,
    string Body,                // written from the evidence, never generic
    DateOnly? DueDate,          // task due / appointment date / the new close date for CloseDateUpdate
    Guid? TargetContactId = null); // the client contact to invite to an appointment; set by the executor

public enum ActionType { Task, Appointment, StageUpdate, CloseDateUpdate }
