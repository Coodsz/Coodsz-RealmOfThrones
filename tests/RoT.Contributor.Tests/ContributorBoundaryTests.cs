using BannerlordCoopLauncher.Core.Support;
using RealmOfThrones.Core;

namespace BannerlordCoopLauncher.Tests;

public class ContributorBoundaryTests
{
    [Fact]
    public void Unsigned_tools_keep_official_update_and_report_actions_off()
    {
        Assert.False(RotRelease.UpdatesReady);
        Assert.False(RotRelease.ReportsReady);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://127.0.0.1.example.com:8080")]
    public void Report_test_workers_must_be_local(string address)
    {
        Assert.Null(TestWorkerUrl.Base(address));
    }
}
