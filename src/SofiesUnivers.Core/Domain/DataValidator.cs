using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Domain;

/// <summary>
/// Validates a complete dataset before it replaces the current data (restore). Checks required
/// fields, value ranges, unique ids and all relationships. Messages are Danish.
/// </summary>
public static class DataValidator
{
    private const int MaxReported = 20;

    /// <param name="availableAttachmentIds">
    /// Attachment ids that are available with the data. Null skips the attachment presence check.
    /// </param>
    public static List<string> Validate(AppData data, IReadOnlySet<Guid>? availableAttachmentIds)
    {
        var errors = new List<string>();

        if (data.DataVersion != AppData.CurrentVersion)
        {
            errors.Add($"Uventet dataversion {data.DataVersion}.");
        }

        ValidateSettings(data.Settings, errors);
        ValidateLookups("sessionselementer", data.ElementTypes, errors);
        ValidateLookups("udgiftskategorier", data.ExpenseCategories, errors);
        ValidateLookups("betalingsmetoder", data.PaymentMethods, errors);

        var elementIds = data.ElementTypes.Select(x => x.Id).ToHashSet();
        var categoryIds = data.ExpenseCategories.Select(x => x.Id).ToHashSet();
        var methodIds = data.PaymentMethods.Select(x => x.Id).ToHashSet();

        CheckUniqueIds("Session", data.Sessions.Select(s => s.Id), errors);
        foreach (var s in data.Sessions)
        {
            var label = $"Sessionen d. {Dk.Date(s.Date)}";
            CheckDate(label, s.Date, errors);
            if (s.ElementIds is null || s.ElementIds.Count is 0 or > Rules.MaxElements)
            {
                errors.Add($"{label} skal have 1-3 sessionselementer.");
            }
            else if (s.ElementIds.Any(id => !elementIds.Contains(id)))
            {
                errors.Add($"{label} henviser til et sessionselement, der ikke findes.");
            }
            else if (s.ElementIds.Distinct().Count() != s.ElementIds.Count)
            {
                errors.Add($"{label} har det samme element flere gange.");
            }

            if (s.DurationMinutes is <= 0 or > Rules.MaxDurationMinutes)
            {
                errors.Add($"{label} har en ugyldig varighed.");
            }

            if (Rules.CheckPrice(s.Price) is { } priceError)
            {
                errors.Add($"{label}: {priceError}");
            }
        }

        var sessions = data.Sessions.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        CheckUniqueIds("Betaling", data.Payments.Select(p => p.Id), errors);
        foreach (var p in data.Payments)
        {
            var label = $"Betalingen d. {Dk.Date(p.Date)}";
            CheckDate(label, p.Date, errors);
            if (!sessions.ContainsKey(p.SessionId))
            {
                errors.Add($"{label} henviser til en session, der ikke findes.");
            }

            if (!methodIds.Contains(p.MethodId))
            {
                errors.Add($"{label} henviser til en betalingsmetode, der ikke findes.");
            }

            if (Rules.CheckPositiveAmount(p.Amount) is { } amountError)
            {
                errors.Add($"{label}: {amountError}");
            }
        }

        foreach (var group in data.Payments.GroupBy(p => p.SessionId))
        {
            if (sessions.TryGetValue(group.Key, out var s) && group.Sum(p => p.Amount) > s.Price)
            {
                errors.Add($"Sessionen d. {Dk.Date(s.Date)} har betalinger, der overstiger prisen.");
            }
        }

        CheckUniqueIds("Udgift", data.Expenses.Select(e => e.Id), errors);
        CheckUniqueIds("Kvittering", data.Expenses.Where(e => e.Attachment is not null).Select(e => e.Attachment!.Id), errors);
        foreach (var e in data.Expenses)
        {
            var label = $"Udgiften d. {Dk.Date(e.Date)}";
            CheckDate(label, e.Date, errors);
            if (!categoryIds.Contains(e.CategoryId))
            {
                errors.Add($"{label} henviser til en kategori, der ikke findes.");
            }

            if (Rules.CheckPositiveAmount(e.Amount) is { } amountError)
            {
                errors.Add($"{label}: {amountError}");
            }

            if (e.Attachment is { } a)
            {
                if (a.SizeBytes <= 0 || string.IsNullOrWhiteSpace(a.ContentType) || string.IsNullOrWhiteSpace(a.FileName))
                {
                    errors.Add($"{label} har ufuldstændige oplysninger om kvitteringen.");
                }
                else if (availableAttachmentIds is not null && !availableAttachmentIds.Contains(a.Id))
                {
                    errors.Add($"{label}: kvitteringen \"{a.FileName}\" mangler i backuppen.");
                }
            }
        }

        CheckUniqueIds("Kørsel", data.Trips.Select(t => t.Id), errors);
        foreach (var t in data.Trips)
        {
            var label = $"Kørslen d. {Dk.Date(t.Date)}";
            CheckDate(label, t.Date, errors);
            if (string.IsNullOrWhiteSpace(t.Purpose) || string.IsNullOrWhiteSpace(t.FromLabel) || string.IsNullOrWhiteSpace(t.ToLabel))
            {
                errors.Add($"{label} mangler formål, start eller mål.");
            }

            if (t.DistanceKm is { } km && (km < 0 || km > Rules.MaxTripKm))
            {
                errors.Add($"{label} har et ugyldigt antal km.");
            }
        }

        foreach (var dup in data.Trips.Where(t => t.IsClinicTrip).GroupBy(t => t.Date).Where(g => g.Count() > 1))
        {
            errors.Add($"Der er mere end én tur til klinikken d. {Dk.Date(dup.Key)}.");
        }

        if (errors.Count > MaxReported)
        {
            var total = errors.Count;
            errors = errors.Take(MaxReported).ToList();
            errors.Add($"… og {total - MaxReported} andre fejl.");
        }

        return errors;
    }

    private static void ValidateSettings(AppSettings? settings, List<string> errors)
    {
        if (settings is null)
        {
            errors.Add("Indstillingerne mangler.");
            return;
        }

        if (settings.Locations is null || settings.Locations.Count == 0)
        {
            errors.Add("Der er ingen steder (hjem/klinik) i indstillingerne.");
            return;
        }

        if (settings.ClinicRoute is null
            || settings.FindLocation(settings.ClinicRoute.FromLocationId) is null
            || settings.FindLocation(settings.ClinicRoute.ToLocationId) is null)
        {
            errors.Add("Standardruten til klinikken henviser til et sted, der ikke findes.");
        }
        else if (Rules.CheckRouteKm(settings.ClinicRoute.RoundTripKm) is not null)
        {
            errors.Add("Standardrutens antal km er ugyldigt.");
        }
    }

    private static void ValidateLookups(string what, IReadOnlyList<LookupItem>? items, List<string> errors)
    {
        if (items is null)
        {
            errors.Add($"Listen over {what} mangler.");
            return;
        }

        CheckUniqueIds(what, items.Select(i => i.Id), errors);
        if (items.Any(i => string.IsNullOrWhiteSpace(i.Name)))
        {
            errors.Add($"Der er {what} uden navn.");
        }

        foreach (var dup in items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).GroupBy(i => Names.Key(i.Name)).Where(g => g.Count() > 1))
        {
            errors.Add($"\"{dup.First().Name}\" findes flere gange blandt {what}.");
        }
    }

    private static void CheckUniqueIds(string what, IEnumerable<Guid> ids, List<string> errors)
    {
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (id == Guid.Empty)
            {
                errors.Add($"{what}: mangler id.");
            }
            else if (!seen.Add(id))
            {
                errors.Add($"{what}: id {id} findes flere gange.");
            }
        }
    }

    private static void CheckDate(string label, DateOnly date, List<string> errors)
    {
        if (Rules.CheckDate(date) is not null)
        {
            errors.Add($"{label} har en ugyldig dato.");
        }
    }
}
