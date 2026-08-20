namespace Zava.SalesIntelligence.Agents;

// Loads a skill's markdown from the embedded resources so agents get the same text
// regardless of the working directory. Skill files live under skills/<name>/.
static class Skills
{
    public static string Load(string skill, string file)
    {
        var assembly = typeof(Skills).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource =>
            resource.Contains($".{skill}.", StringComparison.OrdinalIgnoreCase)
            && resource.EndsWith(file, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
