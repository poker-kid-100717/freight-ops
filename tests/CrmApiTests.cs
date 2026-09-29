using System.Net;
using System.Text.Json;

namespace Portfolio.Freight.Api.Tests;

public sealed class AccountApiTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task CustomersAreRankedByComputedScore()
    {
        var page = await GetJson("/api/customers?pageSize=100");
        var scores = page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("score").GetDecimal()).ToList();

        Assert.Equal(12, page.GetProperty("total").GetInt32());
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public async Task CustomerDetailIncludesEverySection()
    {
        var detail = await GetJson($"/api/customers/{Seeded(103)}");

        Assert.Equal("Canyon Packaging", detail.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal(12, detail.GetProperty("customer").GetProperty("daysSinceTouch").GetInt32());
        foreach (var section in new[] { "contacts", "activities", "followUps", "quotes", "lanes" })
            Assert.True(detail.GetProperty(section).GetArrayLength() > 0, $"{section} should not be empty");
    }

    [Fact]
    public async Task InvalidCustomerReturnsFieldErrors()
    {
        var problem = await PostJson("/api/customers", new { name = "", laneOrigin = "A", laneDestination = "B", stage = "Nope", monthlyLoads = -1 }, 400);
        var errors = problem.GetProperty("errors");

        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("stage", out _));
        Assert.True(errors.TryGetProperty("monthlyLoads", out _));
    }

    [Fact]
    public async Task DuplicateCustomerNameConflicts()
    {
        await PostJson("/api/customers", Customer("Mesa Solar Components"), 409);
    }

    [Fact]
    public async Task CreatedCustomerCanBeEditedAndFound()
    {
        var created = await PostJson("/api/customers", Customer("Portfolio Test Freight"));
        var id = created.GetProperty("id").GetGuid();

        var update = await Client.PutAsJsonAsync($"/api/customers/{id}", Customer("Portfolio Test Freight") with { Stage = "Growth", MonthlyLoads = 7 });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var detail = await GetJson($"/api/customers/{id}");
        Assert.Equal("Growth", detail.GetProperty("customer").GetProperty("stage").GetString());
        var search = await GetJson("/api/search?q=portfolio test");
        Assert.Contains(search.EnumerateArray(), h => h.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task LoggingAnActivityResetsDaysSinceTouch()
    {
        var customer = Seeded(105); // Sandstone Home Goods, last touched 15 days ago
        await PostJson($"/api/customers/{customer}/activities", new { type = "Call", summary = "Checked in on next month's volume." });

        var detail = await GetJson($"/api/customers/{customer}");
        Assert.Equal(0, detail.GetProperty("customer").GetProperty("daysSinceTouch").GetInt32());
        Assert.Equal("Checked in on next month's volume.", detail.GetProperty("activities")[0].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task FutureActivitiesAreRejected()
    {
        await PostJson($"/api/customers/{Seeded(101)}/activities",
            new { type = "Call", summary = "Time travel", occurredAt = DateTime.UtcNow.AddDays(2) }, 400);
    }

    [Fact]
    public async Task FollowUpsCanBeCompletedOnce()
    {
        var created = await PostJson($"/api/customers/{Seeded(101)}/follow-ups",
            new { dueOn = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), description = "Call back" });
        var id = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await Post($"/api/follow-ups/{id}/complete")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/follow-ups/{id}/complete")).StatusCode);
    }

    [Fact]
    public async Task MakingAContactPrimaryDemotesTheOldOne()
    {
        var customer = Seeded(101);
        await PostJson($"/api/customers/{customer}/contacts", new { name = "New Primary", email = "new@example.com", isPrimary = true });

        var contacts = (await GetJson($"/api/customers/{customer}")).GetProperty("contacts").EnumerateArray().ToList();
        Assert.Single(contacts, c => c.GetProperty("isPrimary").GetBoolean());
        Assert.Equal("New Primary", contacts.Single(c => c.GetProperty("isPrimary").GetBoolean()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task LanesCanBeAddedAndDeleted()
    {
        var created = await PostJson($"/api/customers/{Seeded(104)}/lanes",
            new { origin = "Tucson, AZ", destination = "El Paso, TX", equipment = "Dry Van", estimatedLoadsPerMonth = 3, targetRate = 1350 });
        var id = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/lanes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/lanes/{id}")).StatusCode);
    }

    private static CustomerBody Customer(string name) =>
        new(name, "Albuquerque, NM", "Denver, CO", "Prospect", 0, 0, 0, null, null);

    private sealed record CustomerBody(string Name, string LaneOrigin, string LaneDestination, string Stage, int MonthlyLoads,
        decimal MonthlyRevenue, decimal MonthlyGrossMargin, Guid? OwnerId, string? Notes);
}

public sealed class SalesApiTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task QuoteMovesThroughItsLifecycle()
    {
        var customer = Seeded(109); // Turquoise Trail Apparel, a prospect
        var created = await PostJson("/api/quotes", new
        {
            customerId = customer, origin = "Santa Fe, NM", destination = "Los Angeles, CA", equipment = "Dry Van",
            pallets = 10, weight = 9000, rate = 3100, carrierId = Seeded(500), carrierCost = 2600
        });
        var id = created.GetProperty("id").GetGuid();

        var quote = await GetJson($"/api/quotes/{id}");
        Assert.Equal("Draft", quote.GetProperty("status").GetString());
        Assert.Equal(500m, quote.GetProperty("margin").GetDecimal());

        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/quotes/{id}/win")).StatusCode); // draft cannot be won
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"/api/quotes/{id}/send")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/quotes/{id}", new
        {
            customerId = customer, origin = "A", destination = "B", equipment = "Dry Van", pallets = 1, weight = 1, rate = 1
        })).StatusCode); // sent quotes are read-only
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"/api/quotes/{id}/win")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/quotes/{id}/lose")).StatusCode); // closed

        var detail = await GetJson($"/api/customers/{customer}");
        Assert.Equal("Active", detail.GetProperty("customer").GetProperty("stage").GetString()); // first win activates a prospect
        Assert.Contains(detail.GetProperty("activities").EnumerateArray(),
            a => a.GetProperty("summary").GetString()!.Contains(created.GetProperty("number").GetString()!));
    }

    [Fact]
    public async Task QuotesRejectInactiveCarriersAndForeignLanes()
    {
        var problem = await PostJson("/api/quotes", new
        {
            customerId = Seeded(101), laneId = Seeded(402), pallets = 5, weight = 4000, rate = 1500,
            carrierId = Seeded(506), carrierCost = 1200
        }, 400);

        Assert.True(problem.GetProperty("errors").TryGetProperty("laneId", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("carrierId", out _));
    }

    [Fact]
    public async Task QuoteFromLaneInheritsRoute()
    {
        var created = await PostJson("/api/quotes", new { customerId = Seeded(101), laneId = Seeded(450), pallets = 6, weight = 5000, rate = 1300 });
        var quote = await GetJson($"/api/quotes/{created.GetProperty("id").GetGuid()}");

        Assert.Equal("Phoenix, AZ", quote.GetProperty("origin").GetString());
        Assert.Equal("Albuquerque, NM", quote.GetProperty("destination").GetString());
    }

    [Fact]
    public async Task QualifiedLeadConvertsToAccountWithPrimaryContact()
    {
        var lead = Seeded(702); // Bosque Bakery Supply, qualified
        var converted = await PostJson($"/api/leads/{lead}/convert", new { laneOrigin = "Albuquerque, NM", laneDestination = "Denver, CO" }, 200);
        var customerId = converted.GetProperty("customerId").GetGuid();

        var detail = await GetJson($"/api/customers/{customerId}");
        Assert.Equal("Prospect", detail.GetProperty("customer").GetProperty("stage").GetString());
        Assert.Equal("Kai Romero", detail.GetProperty("contacts")[0].GetProperty("name").GetString());
        Assert.Equal("Converted", (await GetJson($"/api/leads/{lead}")).GetProperty("status").GetString());

        // A second conversion is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/leads/{lead}/convert", new { laneOrigin = "A", laneDestination = "B" })).StatusCode);
    }

    [Fact]
    public async Task OnlyQualifiedLeadsConvert()
    {
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post($"/api/leads/{Seeded(700)}/convert", new { laneOrigin = "A", laneDestination = "B" })).StatusCode);
    }

    [Fact]
    public async Task CarrierMcNumbersAreUnique()
    {
        var body = new { name = "Dup", mcNumber = "mc-900101", equipmentTypes = new[] { "Dry Van" }, homeRegion = "NM", status = "Active", rating = 3 };
        await PostJson("/api/carriers", body, 409);
    }
}

public sealed class InsightApiTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task DashboardIsComputedFromData()
    {
        var dashboard = await GetJson("/api/dashboard");

        Assert.Equal(10, dashboard.GetProperty("activeCustomers").GetInt32()); // 12 less one prospect and one inactive
        Assert.Equal(5, dashboard.GetProperty("openOpportunities").GetInt32()); // 2 drafts + 3 sent
        Assert.Equal(5, dashboard.GetProperty("followUpsDue").GetInt32()); // 3 overdue + 2 due today
        Assert.Equal(3, dashboard.GetProperty("followUpsOverdue").GetInt32());
        Assert.Equal(5, dashboard.GetProperty("topOpportunities").GetArrayLength());
    }

    [Fact]
    public async Task MyDayFiltersByRep()
    {
        var casey = await GetJson($"/api/my-day?rep={Seeded(3)}");

        Assert.Equal(2, casey.GetProperty("overdue").GetArrayLength());
        Assert.All(casey.GetProperty("priorityAccounts").EnumerateArray(),
            a => Assert.Equal("Casey Nguyen", a.GetProperty("ownerName").GetString()));
    }

    [Fact]
    public async Task SearchFindsEveryKindOfRecord()
    {
        var types = new Dictionary<string, string>
        {
            ["mesa"] = "Account", ["kai"] = "Lead", ["Q-1007"] = "Quote", ["MC-900103"] = "Carrier", ["morgan"] = "Contact"
        };
        foreach (var (query, type) in types)
        {
            var hits = await GetJson($"/api/search?q={query}");
            Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("type").GetString() == type);
        }
    }

    [Theory]
    [InlineData("revenue-by-customer")]
    [InlineData("quotes-by-month")]
    [InlineData("activity-by-rep")]
    [InlineData("stage-distribution")]
    public async Task ReportsRenderAsJsonAndCsv(string name)
    {
        var report = await GetJson($"/api/reports/{name}");
        Assert.True(report.GetProperty("rows").GetArrayLength() > 0);

        var csv = await Client.GetAsync($"/api/reports/{name}?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var lines = (await csv.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(report.GetProperty("rows").GetArrayLength() + 1, lines.Length);
    }

    [Fact]
    public async Task UnknownReportIs404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/reports/nope")).StatusCode);
    }

    [Fact]
    public async Task ActivityLogFiltersByType()
    {
        var calls = await GetJson("/api/activities?type=Call&pageSize=100");
        Assert.All(calls.GetProperty("items").EnumerateArray(), a => Assert.Equal("Call", a.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task MetaDescribesStorageAndReps()
    {
        var meta = await GetJson("/api/meta");
        Assert.True(meta.GetProperty("storage").GetProperty("ready").GetBoolean());
        Assert.Equal(3, meta.GetProperty("reps").GetArrayLength());
        Assert.Equal(7, meta.GetProperty("stages").GetArrayLength());
    }

    [Fact]
    public async Task HealthAndReadiness()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health")).StatusCode);
        var ready = await GetJson("/health/ready");
        Assert.Equal("Ready", ready.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DemoResetRequiresTheToken()
    {
        await PostJson($"/api/customers/{Seeded(101)}/activities", new { type = "Note", summary = "Temporary" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/admin/reset-demo")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-demo");
        request.Headers.Add("X-Demo-Reset-Token", "test-reset-token");
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(request)).StatusCode);

        var activities = await GetJson($"/api/activities?customerId={Seeded(101)}&pageSize=100");
        Assert.DoesNotContain(activities.GetProperty("items").EnumerateArray(), a => a.GetProperty("summary").GetString() == "Temporary");
    }
}

public sealed class RateLimitedFactory : ApiFactory
{
    protected override int WritesPerMinute => 2;
    protected override string? ResetToken => null;
}

public sealed class RateLimitTests(RateLimitedFactory factory) : IClassFixture<RateLimitedFactory>
{
    [Fact]
    public async Task WritesAreLimitedPerClientButReadsAreNot()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "203.0.113.9");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.PostAsync("/api/admin/reset-demo", null)).StatusCode);

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers")).StatusCode);
    }
}

file static class JsonExtensions
{
    public static Task<HttpResponseMessage> PutAsJsonAsync<T>(this HttpClient client, string url, T body) =>
        System.Net.Http.Json.HttpClientJsonExtensions.PutAsJsonAsync(client, url, body, ApiFactory.Json);
}
