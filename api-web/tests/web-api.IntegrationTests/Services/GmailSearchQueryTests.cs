using infrastructure.Services;

namespace web_api.IntegrationTests.Services;

public class GmailSearchQueryTests
{
    [Fact]
    public void BuildDateRangeQuery_UsesUnixSecondBounds()
    {
        var query = GmailService.BuildDateRangeQuery(
            new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc));

        Assert.Equal("after:1781532000 before:1781618400", query);
    }

    [Fact]
    public void BuildDateRangeQuery_MultiDay_UsesExclusiveEndBound()
    {
        var query = GmailService.BuildDateRangeQuery(
            new DateTime(2026, 6, 9, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc));

        Assert.Equal("after:1781013600 before:1781618400", query);
    }
}
