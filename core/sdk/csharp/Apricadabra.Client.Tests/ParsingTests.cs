using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Apricadabra.Client;

namespace Apricadabra.Client.Tests;

[TestFixture]
public class ParsingTests
{
    [Test]
    public void ParseWelcome_ExtractsApiStatus()
    {
        var json = @"{""type"":""welcome"",""version"":2,""axes"":{""1"":0.5},""buttons"":{""1"":false},""apiStatus"":{""axis"":""exists"",""button"":""deprecated"",""reset"":""undefined""},""coreVersion"":""0.1.0""}";
        var welcome = JsonNode.Parse(json).AsObject();

        var (coreVersion, apiStatus) = ApricadabraClient.ParseWelcome(welcome);

        Assert.That(coreVersion, Is.EqualTo("0.1.0"));
        Assert.That(apiStatus["axis"], Is.EqualTo(ApiStatus.Exists));
        Assert.That(apiStatus["button"], Is.EqualTo(ApiStatus.Deprecated));
        Assert.That(apiStatus["reset"], Is.EqualTo(ApiStatus.Undefined));
    }

    [Test]
    public void ParseWelcome_NoApiStatus_ReturnsNulls()
    {
        var json = @"{""type"":""welcome"",""version"":1,""axes"":{""1"":0.5},""buttons"":{""1"":false}}";
        var welcome = JsonNode.Parse(json).AsObject();

        var (coreVersion, apiStatus) = ApricadabraClient.ParseWelcome(welcome);

        Assert.That(coreVersion, Is.Null);
        Assert.That(apiStatus, Is.Null);
    }

    [Test]
    public void ParseState_ExtractsTypedDictionaries()
    {
        var json = @"{""type"":""state"",""axes"":{""1"":0.75,""3"":0.25},""buttons"":{""1"":true,""5"":false}}";
        var msg = JsonNode.Parse(json).AsObject();

        var (axes, buttons) = ApricadabraClient.ParseState(msg);

        Assert.That(axes[1], Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(axes[3], Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(buttons[1], Is.True);
        Assert.That(buttons[5], Is.False);
    }

    [Test]
    public void ParseState_EmptyAxesAndButtons()
    {
        var json = @"{""type"":""state"",""axes"":{},""buttons"":{}}";
        var msg = JsonNode.Parse(json).AsObject();

        var (axes, buttons) = ApricadabraClient.ParseState(msg);

        Assert.That(axes, Is.Empty);
        Assert.That(buttons, Is.Empty);
    }
}
