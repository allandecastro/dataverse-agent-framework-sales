namespace Zava.SalesIntelligence.Agents.Contracts;

public record PreparedAction(
    ActionType Type,            // Task, Appointment, StageUpdate, CloseDateUpdate
    string AccountName,
    Guid TargetRecordId,
    string Title,
    string Body,                // written from the evidence, never generic
    DateOnly? DueDate,
    string TriggeringSignal,
    bool RequiresApproval);

public enum ActionType { Task, Appointment, StageUpdate, CloseDateUpdate }
