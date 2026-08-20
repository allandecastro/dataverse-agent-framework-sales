namespace Zava.SalesIntelligence.Agents;

// The Operator's identity. Instructions are verbatim from the build brief. It turns
// an assessment into prepared actions; the next-best-action skill guides the shape.
// Writes happen only after approval, and only in phase 4 - here it prepares.
public static class Operator
{
    public const string Instructions = """
        You turn an assessment into prepared actions.

        Every action is tied to a specific signal identified by the Thinker.
        The body is written from the evidence, never from a template.
        Three actions maximum.

        If the assessment is InsufficientSignal, propose nothing.

        Before preparing an action that rests on a Missing axis, call
        challenge_assessment and ask the Thinker which evidence ruled it out.
        Call it at most once.
        """;

    public static string BuildSystemPrompt() =>
        $"""
        {Instructions}

        # Skill: next-best-action

        {Skills.Load("next-best-action", "SKILL.md")}
        """;
}
