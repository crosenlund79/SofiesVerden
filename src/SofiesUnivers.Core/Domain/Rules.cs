using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Domain;

/// <summary>Validation rules shared by the forms, the ledger and backup validation.</summary>
public static class Rules
{
    public const int MaxElements = 3;
    public const int MaxDurationMinutes = 24 * 60;
    public const decimal MaxAmount = 10_000_000m;
    public const decimal MaxTripKm = 5_000m;

    public static readonly DateOnly MinDate = new(2000, 1, 1);
    public static readonly DateOnly MaxDate = new(2100, 12, 31);

    public static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, 2) == value;

    public static string? CheckDate(DateOnly date) =>
        date < MinDate || date > MaxDate ? "Datoen ser forkert ud." : null;

    public static string? CheckPrice(decimal price)
    {
        if (price < 0)
        {
            return "Prisen kan ikke være negativ.";
        }

        if (price > MaxAmount)
        {
            return "Prisen er urealistisk høj.";
        }

        return HasAtMostTwoDecimals(price) ? null : "Brug højst to decimaler.";
    }

    public static string? CheckPositiveAmount(decimal amount)
    {
        if (amount <= 0)
        {
            return "Beløbet skal være større end 0.";
        }

        if (amount > MaxAmount)
        {
            return "Beløbet er urealistisk højt.";
        }

        return HasAtMostTwoDecimals(amount) ? null : "Brug højst to decimaler.";
    }

    public static List<ValidationError> ValidateSession(SessionDraft d, AppData data)
    {
        var errors = new List<ValidationError>();
        Add(errors, "date", CheckDate(d.Date));

        if (d.ElementIds.Count == 0)
        {
            errors.Add(new("elements", "Vælg mindst ét sessionselement."));
        }
        else if (d.ElementIds.Count > MaxElements)
        {
            errors.Add(new("elements", "Du kan højst vælge 3 sessionselementer."));
        }
        else if (d.ElementIds.Distinct().Count() != d.ElementIds.Count)
        {
            errors.Add(new("elements", "Det samme element er valgt to gange."));
        }
        else if (d.ElementIds.Any(id => data.ElementTypes.All(e => e.Id != id)))
        {
            errors.Add(new("elements", "Et af de valgte elementer findes ikke længere."));
        }

        if (d.DurationMinutes <= 0)
        {
            errors.Add(new("duration", "Angiv sessionens varighed."));
        }
        else if (d.DurationMinutes > MaxDurationMinutes)
        {
            errors.Add(new("duration", "Varigheden kan højst være 24 timer."));
        }

        Add(errors, "price", CheckPrice(d.Price));

        if (d.ClientNickname is { Length: > Names.MaxNicknameLength })
        {
            errors.Add(new("nickname", $"Kaldenavnet må højst være {Names.MaxNicknameLength} tegn."));
        }

        for (var i = 0; i < d.Payments.Count; i++)
        {
            var p = d.Payments[i];
            var field = $"payment{i}";
            Add(errors, field, CheckPositiveAmount(p.Amount));
            Add(errors, field, CheckDate(p.Date));
            if (data.PaymentMethods.All(m => m.Id != p.MethodId))
            {
                errors.Add(new(field, "Vælg en betalingsmetode."));
            }
        }

        var paid = d.Payments.Sum(p => p.Amount);
        if (CheckPrice(d.Price) is null && paid > d.Price)
        {
            errors.Add(new("payments", OverpaymentMessage(paid, d.Price)));
        }

        return errors;
    }

    public static string OverpaymentMessage(decimal paid, decimal price) =>
        $"Betalingerne ({Dk.Money(paid)}) er højere end prisen ({Dk.Money(price)}). Ret beløbet eller prisen.";

    public static List<ValidationError> ValidatePayment(PaymentDraft p, Session session, IEnumerable<Payment> otherPayments, AppData data)
    {
        var errors = new List<ValidationError>();
        Add(errors, "amount", CheckPositiveAmount(p.Amount));
        Add(errors, "date", CheckDate(p.Date));
        if (data.PaymentMethods.All(m => m.Id != p.MethodId))
        {
            errors.Add(new("method", "Vælg en betalingsmetode."));
        }

        var paid = otherPayments.Sum(x => x.Amount) + p.Amount;
        if (errors.Count == 0 && paid > session.Price)
        {
            var remaining = Balances.Outstanding(session.Price, paid - p.Amount);
            errors.Add(new("amount", remaining == 0
                ? "Sessionen er allerede betalt fuldt ud."
                : $"Der mangler kun {Dk.Money(remaining)}. Beløbet er for højt."));
        }

        return errors;
    }

    public static List<ValidationError> ValidateExpense(ExpenseDraft d, AppData data)
    {
        var errors = new List<ValidationError>();
        Add(errors, "date", CheckDate(d.Date));
        Add(errors, "amount", CheckPositiveAmount(d.Amount));
        if (data.ExpenseCategories.All(c => c.Id != d.CategoryId))
        {
            errors.Add(new("category", "Vælg en kategori."));
        }

        if (d.Description is { Length: > Names.MaxDescriptionLength })
        {
            errors.Add(new("description", $"Beskrivelsen må højst være {Names.MaxDescriptionLength} tegn."));
        }

        return errors;
    }

    public static List<ValidationError> ValidateTrip(TripDraft d, IEnumerable<Trip> existingTrips)
    {
        var errors = new List<ValidationError>();
        Add(errors, "date", CheckDate(d.Date));
        if (Names.Clean(d.Purpose) is null)
        {
            errors.Add(new("purpose", "Skriv formålet med turen."));
        }
        else if (d.Purpose.Length > Names.MaxLookupLength)
        {
            errors.Add(new("purpose", "Formålet er for langt."));
        }

        if (Names.Clean(d.FromLabel) is null)
        {
            errors.Add(new("from", "Angiv hvor turen starter."));
        }

        if (Names.Clean(d.ToLabel) is null)
        {
            errors.Add(new("to", "Angiv turens mål."));
        }

        if (d.DistanceKm is { } km)
        {
            if (km <= 0)
            {
                errors.Add(new("distance", "Antal km skal være større end 0."));
            }
            else if (km > MaxTripKm)
            {
                errors.Add(new("distance", "Antal km er urealistisk højt."));
            }
            else if (!HasAtMostTwoDecimals(km))
            {
                errors.Add(new("distance", "Brug højst to decimaler."));
            }
        }

        if (d.IsClinicTrip && existingTrips.Any(t => t.IsClinicTrip && t.Date == d.Date && t.Id != d.Id))
        {
            errors.Add(new("date", $"Der er allerede registreret en tur til klinikken d. {Dk.Date(d.Date)}. Åbn den i stedet."));
        }

        return errors;
    }

    public static string? CheckRouteKm(decimal? km)
    {
        if (km is not { } v)
        {
            return null;
        }

        if (v <= 0 || v > MaxTripKm)
        {
            return "Angiv et realistisk antal km.";
        }

        return HasAtMostTwoDecimals(v) ? null : "Brug højst to decimaler.";
    }

    private static void Add(List<ValidationError> errors, string field, string? message)
    {
        if (message is not null)
        {
            errors.Add(new(field, message));
        }
    }
}
