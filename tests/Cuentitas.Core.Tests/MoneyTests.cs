using Cuentitas.Core;

namespace Cuentitas.Core.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(0, "$ 0,00")]
    [InlineData(5, "$ 0,05")]
    [InlineData(123456, "$ 1.234,56")]
    [InlineData(100000000, "$ 1.000.000,00")]
    [InlineData(-250050, "-$ 2.500,50")]
    public void Format_ARS(long cents, string expected) => Assert.Equal(expected, Money.Format(cents));

    [Fact]
    public void Format_USD_with_sign_and_compact()
    {
        Assert.Equal("+US$ 100", Money.Format(10000, Currency.USD, showSign: true, compact: true));
        Assert.Equal("US$ 100,50", Money.Format(10050, Currency.USD, compact: true));
    }

    [Theory]
    [InlineData("1234", 123400)]
    [InlineData("1234,5", 123450)]
    [InlineData("1.234,56", 123456)]
    [InlineData("$ 1.234,56", 123456)]
    [InlineData("1.234.567", 123456700)]
    [InlineData("12.5", 1250)]
    [InlineData("12.50", 1250)]
    [InlineData(",99", 99)]
    [InlineData("-300", -30000)]
    public void Parse_valid(string input, long expected) => Assert.Equal(expected, Money.Parse(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1,234")]
    [InlineData("1,2,3")]
    public void Parse_invalid(string input) => Assert.Null(Money.Parse(input));

    [Fact]
    public void Roundtrip_editable()
    {
        foreach (var cents in new long[] { 0, 1, 99, 100, 123456, 987654321 })
            Assert.Equal(cents, Money.Parse(Money.ToEditable(cents)));
    }

    [Fact]
    public void Format_whole_rounds_to_unit()
    {
        Assert.Equal("$ 80.157", Money.FormatWhole(80_157_14));
        Assert.Equal("$ 80.158", Money.FormatWhole(80_157_50));
        Assert.Equal("+US$ 12", Money.FormatWhole(11_99, Currency.USD, showSign: true));
    }

    [Fact]
    public void Implied_rate_of_dollar_purchase()
    {
        // Pagué $ 120.000 por US$ 100 → $ 1.200 por dólar.
        var rate = Money.ImpliedRate(12_000_000, 10_000);
        Assert.Equal(1200m, rate);
        Assert.Equal("$ 1.200,00", Money.FormatRate(rate));
    }
}
