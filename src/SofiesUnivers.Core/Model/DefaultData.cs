namespace SofiesUnivers.Core.Model;

/// <summary>The initial dataset for a brand new installation.</summary>
public static class DefaultData
{
    public static readonly string[] ElementTypes = ["Massage", "Sexologisk samtale"];
    public static readonly string[] ExpenseCategories = ["Materialer", "Lokaleleje", "Transport", "Markedsføring", "Udstyr", "Andet"];
    public static readonly string[] PaymentMethods = ["MobilePay", "Kontant", "Bankoverførsel"];
    public static readonly string[] TripPurposes = ["Klinik / sessioner", "Rengøring", "Indkøb", "Administration", "Andet"];

    public const string ClinicTripPurpose = "Klinik / sessioner";

    public static AppData Create(DateTimeOffset now)
    {
        var home = new Location { Id = Guid.NewGuid(), Name = "Hjem", Address = "Soluret, Odense SØ", Kind = LocationKind.Home };
        var clinic = new Location { Id = Guid.NewGuid(), Name = "Klinikken", Address = "Hundtofte 14, 5771 Stenstrup", Kind = LocationKind.Clinic };

        return new AppData
        {
            Settings = new AppSettings
            {
                Locations = [home, clinic],
                // The distance is deliberately unknown until verified. Never invent a number.
                ClinicRoute = new ClinicRoute { FromLocationId = home.Id, ToLocationId = clinic.Id, RoundTripKm = null },
            },
            ElementTypes = Lookups(ElementTypes, now),
            ExpenseCategories = Lookups(ExpenseCategories, now),
            PaymentMethods = Lookups(PaymentMethods, now),
            Sessions = [],
            Payments = [],
            Expenses = [],
            Trips = [],
        };
    }

    private static List<LookupItem> Lookups(string[] names, DateTimeOffset now) =>
        names.Select((n, i) => new LookupItem { Id = Guid.NewGuid(), Name = n, SortOrder = i, CreatedAt = now }).ToList();
}
