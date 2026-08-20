namespace Zava.SalesIntelligence.Agents;

// Loads a skill's markdown from the embedded resources so agents get the same text
// regardless of the working directory. Skill files live under skills/<name>/.
static class Skills
{
    public static string Load(string skill, string file)
    {
        // MSBuild turns the hyphen in a folder name into an underscore in the resource name.
        var token = $".{skill.Replace('-', '_')}.";
        var assembly = typeof(Skills).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource =>
            resource.Contains(token, StringComparison.OrdinalIgnoreCase)
            && resource.EndsWith(file, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
