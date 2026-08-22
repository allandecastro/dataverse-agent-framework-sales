using System.Text.Json;
using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Tests.Contracts;

public class ArchetypeJsonConverterTests
{
    static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ArchetypeJsonConverter() },
    };

    static Archetype Read(string json) => JsonSerializer.Deserialize<Archetype>(json, Options);

    [Theory]
    [InlineData("\"Reconnect\"", Archetype.Reconnect)]
    [InlineData("\"AtRisk\"", Archetype.AtRisk)]
    [InlineData("\"ReadyToClose\"", Archetype.ReadyToClose)]
    [InlineData("\"slipping\"", Archetype.Slipping)] // case-insensitive
    public void Parses_archetype_names(string json, Archetype expected) =>
        Assert.Equal(expected, Read(json));

    [Theory]
    [InlineData("\"InsufficientSignal\"")] // a Confidence value, not an Archetype - the refusal case
    [InlineData("\"At-Risk\"")]            // malformed
    [InlineData("\"\"")]                    // empty
    [InlineData("\"nonsense\"")]
    public void Falls_back_to_Reconnect_on_a_non_archetype_string(string json) =>
        Assert.Equal(Archetype.Reconnect, Read(json));

    [Fact]
    public void Parses_an_in_range_numeric_archetype() =>
        Assert.Equal(Archetype.ReadyToClose, Read("2")); // Reconnect=0, AtRisk=1, ReadyToClose=2, Slipping=3

    [Fact]
    public void Falls_back_to_Reconnect_on_an_out_of_range_number() =>
        Assert.Equal(Archetype.Reconnect, Read("9"));

    [Fact]
    public void Writes_the_archetype_as_its_name() =>
        Assert.Equal("\"Slipping\"", JsonSerializer.Serialize(Archetype.Slipping, Options));
}
