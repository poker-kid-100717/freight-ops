using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Portfolio.Freight.Api.Models;

namespace Portfolio.Freight.Api.Integrations.Alvys;

public interface IExternalLoadReader
{
    IntegrationStatus Status { get; }
    Task<ExternalLoadResult> GetVisibleLoadsAsync(CancellationToken ct);
}

public sealed class AlvysLoadReader(
    IHttpClientFactory clients,
    IOptions<AlvysOptions> options,
    AlvysTokenProvider tokens,
    ILogger<AlvysLoadReader> logger) : IExternalLoadReader
{
    public const string ApiClient = "alvys-api";
    private readonly AlvysOptions cfg = options.Value;

    public IntegrationStatus Status => new(
        "Alvys Public API",
        cfg.LiveConfigured ? "Live" : "Demo",
        cfg.LiveConfigured,
        "Read-only public API adapter; credentials and bearer tokens remain server-side.");

    public async Task<ExternalLoadResult> GetVisibleLoadsAsync(CancellationToken ct)
    {
        if (!cfg.LiveConfigured) return Demo();

        try
        {
            var token = await tokens.GetAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{cfg.ApiBaseUrl.TrimEnd('/')}/{cfg.LoadApiVersion}/loads/search");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                Page = 0,
                PageSize = 25,
                Status = new[] { "Open", "Quoted", "Reserved", "Covered", "Dispatched", "In Transit" },
                IncludeDeleted = false
            });

            using var response = await clients.CreateClient(ApiClient).SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<LoadSearchResponse>(cancellationToken: ct);
            var loads = (result?.Items ?? [])
                .Select(x => new ExternalLoad(
                    x.LoadNumber ?? x.Id ?? "unknown",
                    x.CustomerName ?? "Customer not supplied",
                    x.Status ?? "Unknown",
                    x.ScheduledPickupAt,
                    x.ScheduledDeliveryAt,
                    x.RequiredEquipment ?? [],
                    x.Weight?.Value,
                    "Alvys Public API"))
                .ToArray();
            return new("Alvys Public API", true, false, null, loads);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Alvys read degraded; returning synthetic portfolio data.");
            var demo = Demo();
            return demo with { Degraded = true, DegradedReason = "Live Alvys read was unavailable; synthetic portfolio data is shown instead." };
        }
    }

    private static ExternalLoadResult Demo() => new(
        "Synthetic portfolio provider",
        false,
        false,
        null,
        [
            new("LD-24018", "Mesa Solar Components", "Open", DateTimeOffset.UtcNow.AddHours(5), DateTimeOffset.UtcNow.AddDays(1), ["Dry Van"], 18_200m, "Synthetic"),
            new("LD-24021", "High Desert Foods", "Covered", DateTimeOffset.UtcNow.AddHours(8), DateTimeOffset.UtcNow.AddDays(2), ["Reefer"], 31_500m, "Synthetic"),
            new("LD-24026", "Canyon Packaging", "In Transit", DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(11), ["Dry Van"], 22_400m, "Synthetic")
        ]);

    private sealed record LoadSearchResponse(int Page, int PageSize, int Total, List<LoadItem>? Items);
    private sealed record LoadItem(
        string? Id,
        string? LoadNumber,
        string? CustomerName,
        string? Status,
        DateTimeOffset? ScheduledPickupAt,
        DateTimeOffset? ScheduledDeliveryAt,
        List<string>? RequiredEquipment,
        Quantity? Weight);
    private sealed record Quantity(decimal? Value, string? UnitOfMeasure);
}
