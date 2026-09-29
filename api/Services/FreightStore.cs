using Portfolio.Freight.Api.Models;

namespace Portfolio.Freight.Api.Services;

public sealed class FreightStore
{
    public IReadOnlyList<CustomerAccount> Customers { get; } =
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Mesa Solar Components", "Albuquerque, NM -> Phoenix, AZ", 28, 118_000m, 18_400m, 9, "Expansion"),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "High Desert Foods", "Santa Fe, NM -> Denver, CO", 19, 91_000m, 13_900m, 3, "Active"),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Canyon Packaging", "El Paso, TX -> Dallas, TX", 34, 142_000m, 21_300m, 12, "At Risk"),
        new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Rio Valley Medical Supply", "Las Cruces, NM -> Tucson, AZ", 13, 76_000m, 11_200m, 6, "Growth"),
        new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Sandstone Home Goods", "Denver, CO -> Albuquerque, NM", 11, 64_000m, 8_900m, 15, "Reactivation")
    ];
}
