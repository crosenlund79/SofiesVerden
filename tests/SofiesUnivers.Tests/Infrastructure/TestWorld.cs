using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Tests.Infrastructure;

/// <summary>A ledger on an in-memory store with handy shortcuts for tests.</summary>
public sealed class TestWorld
{
    private TestWorld(InMemoryStore store, TestClock clock, Ledger ledger)
    {
        Store = store;
        Clock = clock;
        Ledger = ledger;
    }

    public InMemoryStore Store { get; }
    public TestClock Clock { get; }
    public Ledger Ledger { get; private set; }

    public AppData Data => Ledger.Data;

    public static async Task<TestWorld> CreateAsync(decimal? routeKm = 42m)
    {
        var store = new InMemoryStore();
        var clock = new TestClock();
        var ledger = await Ledger.LoadAsync(store, clock);
        var world = new TestWorld(store, clock, ledger);
        if (routeKm is { } km)
        {
            var s = ledger.Data.Settings;
            (await ledger.UpdateSettingsAsync(s with { ClinicRoute = s.ClinicRoute with { RoundTripKm = km } })).AssertOk();
        }

        return world;
    }

    /// <summary>Simulates closing and reopening the app.</summary>
    public async Task RestartAsync() => Ledger = await Ledger.LoadAsync(Store, Clock);

    public Guid Element(string name) => Data.ElementTypes.Single(e => e.Name == name).Id;
    public Guid Method(string name) => Data.PaymentMethods.Single(e => e.Name == name).Id;
    public Guid Category(string name) => Data.ExpenseCategories.Single(e => e.Name == name).Id;

    public Guid Massage => Element("Massage");
    public Guid MobilePay => Method("MobilePay");
    public Guid Cash => Method("Kontant");

    public SessionDraft Draft(DateOnly date, decimal price, params PaymentDraft[] payments) => new()
    {
        Date = date,
        ElementIds = [Massage],
        DurationMinutes = 120,
        Price = price,
        Payments = payments,
    };

    public PaymentDraft Pay(decimal amount, DateOnly? date = null, Guid? method = null) =>
        new() { Date = date ?? Clock.Today, Amount = amount, MethodId = method ?? MobilePay };

    public async Task<Session> AddSessionAsync(DateOnly date, decimal price = 800m, string? nickname = null, params PaymentDraft[] payments)
    {
        var result = await Ledger.SaveSessionAsync(Draft(date, price, payments) with { ClientNickname = nickname });
        return result.AssertOk();
    }

    public async Task<Session> AddPaidSessionAsync(DateOnly date, decimal price = 800m, string? nickname = null) =>
        await AddSessionAsync(date, price, nickname, Pay(price, date));
}

public static class ResultExtensions
{
    public static T AssertOk<T>(this SaveResult<T> result)
    {
        Assert.True(result.Ok, "Expected success but got: " + string.Join("; ", result.Errors.Select(e => $"{e.Field}: {e.Message}")));
        return result.Value!;
    }

    public static void AssertError<T>(this SaveResult<T> result, string field)
    {
        Assert.False(result.Ok, "Expected a validation error");
        Assert.Contains(result.Errors, e => e.Field == field);
    }
}

public static class Dates
{
    public static DateOnly D(int year, int month, int day) => new(year, month, day);
}
