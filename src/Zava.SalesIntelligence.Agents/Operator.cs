namespace Zava.SalesIntelligence.Agents;

// The Operator's identity. It turns an assessment into prepared actions; the next-best-action
// skill guides the shape. Writes happen only after approval, in the execute stage - here it only
// prepares. The challenge of a Missing axis is orchestrated by the workflow (the Operator executor
// routes to the Thinker), not called by this agent - so its instructions do not name a tool.
public static class Operator
{
    public const string Instructions = """
        You turn an assessment into prepared actions.

        Every action is tied to a specific signal identified by the Thinker.
        The body is written from the evidence, never from a template.
        Three actions maximum.

        If the assessment is InsufficientSignal, propose nothing.

        Do not build an action on a BANT axis the Thinker marked Missing unless the
        evidence has been questioned - see the next-best-action skill.
        """;

    public static string BuildSystemPrompt() =>
        $"""
        {Instructions}

        # Skill: next-best-action

        {Skills.Load("next-best-action", "SKILL.md")}
        """;
}
