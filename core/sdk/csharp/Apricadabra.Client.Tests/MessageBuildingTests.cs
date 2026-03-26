using System.Text.Json.Nodes;
using NUnit.Framework;
using Apricadabra.Client;

namespace Apricadabra.Client.Tests;

[TestFixture]
public class MessageBuildingTests
{
    [Test]
    public void BuildAxisMessage_Hold_CorrectJson()
    {
        var json = ApricadabraClient.BuildAxisMessage(1, AxisMode.Hold, 3, 0.02f);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["type"].GetValue<string>(), Is.EqualTo("axis"));
        Assert.That(obj["axis"].GetValue<int>(), Is.EqualTo(1));
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("hold"));
        Assert.That(obj["diff"].GetValue<int>(), Is.EqualTo(3));
        Assert.That(obj["sensitivity"].GetValue<float>(), Is.EqualTo(0.02f).Within(0.001f));
    }

    [Test]
    public void BuildAxisMessage_Spring_IncludesDecayRate()
    {
        var json = ApricadabraClient.BuildAxisMessage(2, AxisMode.Spring, -1, 0.02f, decayRate: 0.95f);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("spring"));
        Assert.That(obj["decayRate"].GetValue<float>(), Is.EqualTo(0.95f).Within(0.001f));
    }

    [Test]
    public void BuildAxisMessage_Detent_IncludesSteps()
    {
        var json = ApricadabraClient.BuildAxisMessage(3, AxisMode.Detent, 1, 0.02f, steps: 5);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("detent"));
        Assert.That(obj["steps"].GetValue<int>(), Is.EqualTo(5));
    }

    [Test]
    public void BuildButtonMessage_Pulse_NoState()
    {
        var json = ApricadabraClient.BuildButtonMessage(5, ButtonMode.Pulse);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["type"].GetValue<string>(), Is.EqualTo("button"));
        Assert.That(obj["button"].GetValue<int>(), Is.EqualTo(5));
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("pulse"));
        Assert.That(obj["state"], Is.Null);
    }

    [Test]
    public void BuildButtonMessage_Momentary_IncludesState()
    {
        var json = ApricadabraClient.BuildButtonMessage(1, ButtonMode.Momentary, state: ButtonState.Down);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["state"].GetValue<string>(), Is.EqualTo("down"));
    }

    [Test]
    public void BuildButtonMessage_Double_IncludesDelay()
    {
        var json = ApricadabraClient.BuildButtonMessage(4, ButtonMode.Double, delay: 80);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("double"));
        Assert.That(obj["delay"].GetValue<int>(), Is.EqualTo(80));
    }

    [Test]
    public void BuildButtonMessage_Rapid_IncludesRate()
    {
        var json = ApricadabraClient.BuildButtonMessage(5, ButtonMode.Rapid, state: ButtonState.Down, rate: 100);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["rate"].GetValue<int>(), Is.EqualTo(100));
    }

    [Test]
    public void BuildButtonMessage_LongShort_IncludesAllFields()
    {
        var json = ApricadabraClient.BuildButtonMessage(6, ButtonMode.LongShort,
            state: ButtonState.Down, shortButton: 6, longButton: 7, threshold: 500);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("longshort"));
        Assert.That(obj["shortButton"].GetValue<int>(), Is.EqualTo(6));
        Assert.That(obj["longButton"].GetValue<int>(), Is.EqualTo(7));
        Assert.That(obj["threshold"].GetValue<int>(), Is.EqualTo(500));
    }

    [Test]
    public void BuildResetMessage_CorrectJson()
    {
        var json = ApricadabraClient.BuildResetMessage(1, 0.5f);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["type"].GetValue<string>(), Is.EqualTo("reset"));
        Assert.That(obj["axis"].GetValue<int>(), Is.EqualTo(1));
        Assert.That(obj["position"].GetValue<float>(), Is.EqualTo(0.5f).Within(0.001f));
    }
}
