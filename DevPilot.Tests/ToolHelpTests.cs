using DevPilot.Core.Help;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class ToolHelpTests
{
    [Fact]
    public void Every_real_kind_has_help_with_examples()
    {
        foreach (var kind in Enum.GetValues<ToolKind>().Where(k => k != ToolKind.Separator))
        {
            var topics = ToolHelp.ForKind(kind);
            Assert.NotEmpty(topics);
            Assert.Contains(topics, t => t.HasExamples);
        }
    }

    [Fact]
    public void Separator_has_no_field_help() => Assert.Empty(ToolHelp.ForKind(ToolKind.Separator));

    [Fact]
    public void Topics_are_complete()
    {
        var all = Enum.GetValues<ToolKind>().SelectMany(ToolHelp.ForKind).Concat(ToolHelp.Common);
        foreach (var t in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.False(string.IsNullOrWhiteSpace(t.Text));
        }
    }

    [Fact]
    public void Common_help_lists_every_token()
    {
        var tokens = ToolHelp.Common.First(t => t.Title == "Tokens").Text;
        foreach (var token in new[] { "{ToolDir}", "{Home}", "{Pictures}", "{Documents}", "{Desktop}", "{AppData}", "{LocalAppData}" })
            Assert.Contains(token, tokens);
    }

    [Fact]
    public void Examples_text_has_one_line_per_example()
    {
        var topic = new HelpTopic("x", "y", ["a", "b"]);
        Assert.Equal(new[] { "a", "b" }, topic.ExamplesText.Split(Environment.NewLine));
    }
}
