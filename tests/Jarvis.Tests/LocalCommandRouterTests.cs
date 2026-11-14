using Jarvis.Windows;
using Xunit;

namespace Jarvis.Tests;

public sealed class LocalCommandRouterTests
{
    private readonly LocalCommandRouter _router = new();

    [Theory]
    [InlineData("calculate 17 * 23", "391")]
    [InlineData("what is 20 / 4?", "5")]
    [InlineData("calculate 2 + 2", "4")]
    public async Task CalculatesSimpleArithmeticWithoutAi(string input, string expected)
    {
        var result = await _router.TryRunAsync(input);
        Assert.True(result.Handled);
        Assert.Equal(expected, result.Response);
    }

    [Fact]
    public async Task DoesNotTreatUnrecognizedTextAsLocalCommand()
    {
        var result = await _router.TryRunAsync("Explain eigenvectors in simple terms");
        Assert.False(result.Handled);
    }

    [Fact]
    public async Task DoesNotEvaluateDivisionByZero()
    {
        var result = await _router.TryRunAsync("calculate 1 / 0");
        Assert.False(result.Handled);
    }
}
