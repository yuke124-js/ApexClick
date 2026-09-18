using System.Text.Json;
using ApexClick.Models;

namespace ApexClick.Models.Tests;

public class PayloadConvertTests
{
    [Fact]
    public void TryGetInt32_HandlesRawInt()
    {
        Assert.Equal(120, PayloadConvert.TryGetInt32(120));
    }

    [Fact]
    public void TryGetInt32_HandlesJsonElement()
    {
        var element = JsonDocument.Parse("-120").RootElement;
        Assert.Equal(-120, PayloadConvert.TryGetInt32(element));
    }

    [Fact]
    public void TryGetInt32_ReturnsNullForNullOrWrongType()
    {
        Assert.Null(PayloadConvert.TryGetInt32(null));
        Assert.Null(PayloadConvert.TryGetInt32("not a number"));
    }

    [Fact]
    public void TryGetString_HandlesRawAndJsonElement()
    {
        Assert.Equal("hello", PayloadConvert.TryGetString("hello"));
        var element = JsonDocument.Parse("\"hello\"").RootElement;
        Assert.Equal("hello", PayloadConvert.TryGetString(element));
    }
}
