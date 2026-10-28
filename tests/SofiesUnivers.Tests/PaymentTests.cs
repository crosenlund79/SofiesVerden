using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Reports;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class PaymentTests
{
    [Fact]
    public async Task Full_payment_leaves_nothing_outstanding()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddPaidSessionAsync(D(2026, 10, 28), 800m);

        Assert.Equal(0m, w.Data.OutstandingFor(s));
        Assert.Empty(ReportCalculator.OutstandingSessions(w.Data));
    }

    [Fact]
    public async Task No_payment_means_everything_is_outstanding()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 28), 800m);

        Assert.Equal(800m, w.Data.OutstandingFor(s));
        Assert.Single(ReportCalculator.OutstandingSessions(w.Data));
    }

    [Fact]
    public async Task Partial_payments_with_different_methods()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 28), 800m, null, w.Pay(300m, method: w.MobilePay), w.Pay(200m, method: w.Cash));

        Assert.Equal(500m, w.Data.PaidFor(s.Id));
        Assert.Equal(300m, w.Data.OutstandingFor(s));
        Assert.Equal(2, w.Data.Payments.Select(p => p.MethodId).Distinct().Count());
    }

    [Fact]
    public async Task Later_payment_is_registered_with_its_own_date()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1), 800m, null, w.Pay(500m, D(2026, 10, 1)));

        var later = (await w.Ledger.AddPaymentAsync(s.Id, w.Pay(300m, D(2026, 10, 20), w.Method("Bankoverførsel")))).AssertOk();

        Assert.Equal(D(2026, 10, 20), later.Date);
        Assert.Equal(0m, w.Data.OutstandingFor(s));
        Assert.Equal(D(2026, 10, 1), w.Data.Sessions.Single().Date);
    }

    [Fact]
    public async Task Overpayment_is_prevented()
    {
        var w = await TestWorld.CreateAsync();
        (await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 1), 800m, w.Pay(900m)))).AssertError("payments");
        Assert.Empty(w.Data.Sessions);

        var s = await w.AddSessionAsync(D(2026, 10, 1), 800m, null, w.Pay(500m));
        (await w.Ledger.AddPaymentAsync(s.Id, w.Pay(301m))).AssertError("amount");
        (await w.Ledger.AddPaymentAsync(s.Id, w.Pay(300m))).AssertOk();
        (await w.Ledger.AddPaymentAsync(s.Id, w.Pay(1m))).AssertError("amount");
    }

    [Fact]
    public async Task Lowering_the_price_below_paid_amount_is_rejected()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddPaidSessionAsync(D(2026, 10, 1), 800m);
        var payment = w.Data.Payments.Single();

        var result = await w.Ledger.SaveSessionAsync(w.Draft(s.Date, 700m, w.Pay(800m) with { Id = payment.Id }) with { Id = s.Id });

        result.AssertError("payments");
        Assert.Equal(800m, w.Data.Sessions.Single().Price);
    }

    [Fact]
    public async Task Editing_a_session_does_not_silently_change_payments()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1), 800m, null, w.Pay(800m, D(2026, 10, 1)));
        var payment = w.Data.Payments.Single();

        // Moving the session to another date keeps the payment exactly as it was.
        var draft = w.Draft(D(2026, 10, 3), 800m, w.Pay(800m, D(2026, 10, 1)) with { Id = payment.Id }) with { Id = s.Id };
        (await w.Ledger.SaveSessionAsync(draft)).AssertOk();

        var after = w.Data.Payments.Single();
        Assert.Equal(payment, after);
    }

    [Fact]
    public async Task Editing_and_deleting_payments()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1), 800m, null, w.Pay(300m));
        var payment = w.Data.Payments.Single();

        (await w.Ledger.UpdatePaymentAsync(payment.Id, w.Pay(400m, D(2026, 10, 2), w.Cash))).AssertOk();
        Assert.Equal(400m, w.Data.PaidFor(s.Id));
        Assert.Equal(w.Cash, w.Data.Payments.Single().MethodId);
        (await w.Ledger.UpdatePaymentAsync(payment.Id, w.Pay(801m))).AssertError("amount");

        var deleted = await w.Ledger.DeletePaymentAsync(payment.Id);
        Assert.Equal(800m, w.Data.OutstandingFor(s));
        await w.Ledger.UndoDeleteAsync(deleted!);
        Assert.Equal(400m, w.Data.PaidFor(s.Id));
    }

    [Fact]
    public async Task Removing_a_payment_in_the_session_form_deletes_only_that_payment()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1), 800m, null, w.Pay(300m), w.Pay(200m, method: w.Cash));
        var keep = w.Data.Payments.First(p => p.Amount == 300m);

        var draft = w.Draft(s.Date, 800m, w.Pay(300m) with { Id = keep.Id }) with { Id = s.Id };
        (await w.Ledger.SaveSessionAsync(draft)).AssertOk();

        Assert.Equal(keep.Id, w.Data.Payments.Single().Id);
    }

    [Fact]
    public async Task Cross_year_payment_counts_in_the_payment_year()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2025, 12, 20), 1000m, null, w.Pay(400m, D(2025, 12, 20)));
        (await w.Ledger.AddPaymentAsync(s.Id, w.Pay(600m, D(2026, 1, 10)))).AssertOk();

        var y2025 = ReportCalculator.ForYear(w.Data, 2025);
        var y2026 = ReportCalculator.ForYear(w.Data, 2026);

        Assert.Equal(1000m, y2025.SessionValue);
        Assert.Equal(400m, y2025.PaymentsReceived);
        Assert.Equal(0m, y2025.OutstandingOnYearSessions);
        Assert.Equal(0m, y2026.SessionValue);
        Assert.Equal(600m, y2026.PaymentsReceived);
    }

    [Fact]
    public async Task Payment_requires_positive_amount_and_known_method()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1), 800m);
        (await w.Ledger.AddPaymentAsync(s.Id, w.Pay(0m))).AssertError("amount");
        (await w.Ledger.AddPaymentAsync(s.Id, w.Pay(100m) with { MethodId = Guid.NewGuid() })).AssertError("method");
    }
}
