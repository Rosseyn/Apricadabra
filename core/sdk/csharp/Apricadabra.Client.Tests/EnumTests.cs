using NUnit.Framework;
using Apricadabra.Client;

namespace Apricadabra.Client.Tests;

[TestFixture]
public class EnumTests
{
    [TestCase(AxisMode.Hold, "hold")]
    [TestCase(AxisMode.Spring, "spring")]
    [TestCase(AxisMode.Detent, "detent")]
    public void AxisMode_ToWireString_ReturnsSnakeCase(AxisMode mode, string expected)
    {
        Assert.That(mode.ToWireString(), Is.EqualTo(expected));
    }

    [TestCase(ButtonMode.Momentary, "momentary")]
    [TestCase(ButtonMode.Toggle, "toggle")]
    [TestCase(ButtonMode.Pulse, "pulse")]
    [TestCase(ButtonMode.Double, "double")]
    [TestCase(ButtonMode.Rapid, "rapid")]
    [TestCase(ButtonMode.LongShort, "longshort")]
    public void ButtonMode_ToWireString_ReturnsSnakeCase(ButtonMode mode, string expected)
    {
        Assert.That(mode.ToWireString(), Is.EqualTo(expected));
    }

    [TestCase(ButtonState.Down, "down")]
    [TestCase(ButtonState.Up, "up")]
    public void ButtonState_ToWireString_ReturnsSnakeCase(ButtonState state, string expected)
    {
        Assert.That(state.ToWireString(), Is.EqualTo(expected));
    }

    [TestCase("exists", ApiStatus.Exists)]
    [TestCase("deprecated", ApiStatus.Deprecated)]
    [TestCase("undefined", ApiStatus.Undefined)]
    public void ApiStatus_Parse_FromWireString(string wire, ApiStatus expected)
    {
        Assert.That(EnumExtensions.ParseApiStatus(wire), Is.EqualTo(expected));
    }
}
