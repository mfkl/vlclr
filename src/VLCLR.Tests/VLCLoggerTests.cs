using Xunit;

namespace VLCLR.Tests;

public class VLCLoggerTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("100% done", "100%% done")]
    [InlineData("%s %d %%", "%%s %%d %%%%")]
    [InlineData("", "")]
    public void ToLiteralFormat_EscapesEveryPercentSign(string message, string format)
    {
        Assert.Equal(format, VLCLogger.ToLiteralFormat(message));
    }
}
