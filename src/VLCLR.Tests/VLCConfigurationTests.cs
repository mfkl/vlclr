using VLCLR.Plugin;
using Xunit;

namespace VLCLR.Tests;

public class VLCConfigurationTests
{
    private readonly VLCConfiguration _configuration = new(0);

    [Fact]
    public void GetBool_NullObject_ReturnsFallback() =>
        Assert.True(_configuration.GetBool("option", true));

    [Fact]
    public void GetInteger_NullObject_ReturnsFallback() =>
        Assert.Equal(42, _configuration.GetInteger("option", 42));

    [Fact]
    public void GetFloat_NullObject_ReturnsFallback() =>
        Assert.Equal(0.75f, _configuration.GetFloat("option", 0.75f));

    [Fact]
    public void GetString_NullObject_ReturnsFallback() =>
        Assert.Equal("fallback", _configuration.GetString("option", "fallback"));
}
