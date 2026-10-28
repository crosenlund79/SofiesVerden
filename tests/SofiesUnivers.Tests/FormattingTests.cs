using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(0, "0,00 kr.")]
    [InlineData(800, "800,00 kr.")]
    [InlineData(1234.5, "1.234,50 kr.")]
    [InlineData(1234567.89, "1.234.567,89 kr.")]
    public void Money_uses_Danish_separators(decimal amount, string expected) => Assert.Equal(expected, Dk.Money(amount));

    [Theory]
    [InlineData("800", 800)]
    [InlineData("800,50", 800.50)]
    [InlineData("800,5", 800.5)]
    [InlineData("1.250", 1250)]
    [InlineData("1.250,75", 1250.75)]
    [InlineData("12.500.000", 12500000)]
    [InlineData("1250.5", 1250.5)]
    [InlineData(" 800 kr. ", 800)]
    [InlineData("800 kr", 800)]
    [InlineData("1 250", 1250)]
    public void Parses_amounts_typed_by_a_Danish_user(string input, decimal expected)
    {
        Assert.True(Dk.TryParseAmount(input, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-50")]
    [InlineData("1,2,3")]
    [InlineData("10,123")]
    [InlineData("1.25.0")]
    [InlineData("12.3456")]
    public void Rejects_invalid_amounts(string input) => Assert.False(Dk.TryParseAmount(input, out _));

    [Theory]
    [InlineData(120, "2 timer")]
    [InlineData(150, "2½ timer")]
    [InlineData(180, "3 timer")]
    [InlineData(60, "1 time")]
    [InlineData(90, "1½ time")]
    [InlineData(75, "1 t. 15 min.")]
    [InlineData(45, "45 min.")]
    public void Formats_durations(int minutes, string expected) => Assert.Equal(expected, Dk.Duration(minutes));

    [Fact]
    public void Formats_dates_in_Danish()
    {
        var date = new DateOnly(2026, 10, 28);
        Assert.Equal("28. okt. 2026", Dk.Date(date));
        Assert.Equal("28-10-2026", Dk.DateNumeric(date));
        Assert.Equal("onsdag 28. oktober 2026", Dk.DateLong(date));
        Assert.Equal("Oktober 2026", Dk.MonthYear(2026, 10));
    }

    [Fact]
    public void Formats_km_and_input_amounts()
    {
        Assert.Equal("42,5 km", Dk.Km(42.5m));
        Assert.Equal("42 km", Dk.Km(42m));
        Assert.Equal("1234,5", Dk.InputAmount(1234.50m));
        Assert.Equal("800", Dk.InputAmount(800m));
    }

    [Fact]
    public void Danish_sort_order_puts_æøå_last()
    {
        var sorted = new[] { "Åben", "Ægte", "Zebra", "Øl", "Andet" }.OrderBy(x => x, DanishComparer.Instance).ToArray();
        Assert.Equal(["Andet", "Zebra", "Ægte", "Øl", "Åben"], sorted);
    }

    [Theory]
    [InlineData("  Massage  ", "Massage")]
    [InlineData("Sexologisk    samtale", "Sexologisk samtale")]
    [InlineData("   ", null)]
    public void Cleans_names(string input, string? expected) => Assert.Equal(expected, Names.Clean(input));

    [Fact]
    public void Names_that_only_differ_by_case_or_spacing_are_the_same()
    {
        Assert.True(Names.SameName("massage", " MASSAGE "));
        Assert.True(Names.SameName("Sexologisk  samtale", "sexologisk samtale"));
        Assert.False(Names.SameName("Massage", "Massager"));
    }
}
