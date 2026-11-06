using DevPilot.Core.Repos;
using Xunit;

namespace DevPilot.Tests;

public sealed class RepoRemoteTests
{
    [Theory]
    [InlineData("https://someone@github.com/owner/repo.git", "https://github.com/owner/repo.git")]
    [InlineData("https://someone:secret@github.com/owner/repo.git", "https://github.com/owner/repo.git")]
    [InlineData("HTTP://someone@host/x", "HTTP://host/x")]
    [InlineData("  https://someone@github.com  ", "https://github.com")]
    public void The_user_part_of_an_http_address_is_removed(string url, string expected) =>
        Assert.Equal(expected, RepoRemote.StripUser(url));

    [Theory]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("https://github.com/owner/repo@2.git")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_addresses_are_left_alone(string? url) => Assert.Null(RepoRemote.StripUser(url));
}
