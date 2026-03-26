using NUnit.Framework;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Gestures;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Apricadabra.Trackpad.Core.Tests;

[TestFixture]
public class BindingEngineTests
{
    private BindingEngine _engine;
    private List<(string method, object[] args)> _sentCommands;

    [SetUp]
    public void Setup()
    {
        _sentCommands = new List<(string, object[])>();
        var config = new BindingConfig
        {
            Bindings = new List<BindingEntry>
            {
                MakeBinding("scroll-up", "scroll", 2, "up", "axis", axis: 1, mode: "hold"),
                MakeBinding("swipe-3-left", "swipe", 3, "left", "button", button: 5, mode: "pulse"),
                MakeBinding("tap-2", "tap", 2, "none", "button", button: 10, mode: "momentary"),
                MakeBinding("pinch-out", "pinch", 2, "out", "axis", axis: 3, mode: "spring"),
            }
        };
        _engine = new BindingEngine(config);
        _engine.OnSendAxis += (axis, mode, diff, sens, decay, steps) =>
            _sentCommands.Add(("axis", new object[] { axis, mode, diff }));
        _engine.OnSendButton += (button, mode, state) =>
            _sentCommands.Add(("button", new object[] { button, mode, state }));
    }

    [Test]
    public void ScrollUp_MatchesAxisBinding_SendsAxis()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Scroll, 2, GestureDirection.Up, GesturePhase.Update, 0.05f));
        Assert.That(_sentCommands, Has.Count.GreaterThan(0));
        Assert.That(_sentCommands[0].method, Is.EqualTo("axis"));
    }

    [Test]
    public void SwipeLeft_MatchesButtonBinding_SendsButtonOnEnd()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Swipe, 3, GestureDirection.Left, GesturePhase.End));
        Assert.That(_sentCommands, Has.Count.EqualTo(1));
        Assert.That(_sentCommands[0].method, Is.EqualTo("button"));
    }

    [Test]
    public void TapBegin_MomentaryButton_SendsDown()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Tap, 2, GestureDirection.None, GesturePhase.Begin));
        Assert.That(_sentCommands, Has.Count.EqualTo(1));
        Assert.That(_sentCommands[0].args[2], Is.EqualTo("down"));
    }

    [Test]
    public void TapEnd_MomentaryButton_SendsUp()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Tap, 2, GestureDirection.None, GesturePhase.End));
        Assert.That(_sentCommands, Has.Count.EqualTo(1));
        Assert.That(_sentCommands[0].args[2], Is.EqualTo("up"));
    }

    [Test]
    public void NoMatchingBinding_DoesNothing()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Swipe, 4, GestureDirection.Right, GesturePhase.End));
        Assert.That(_sentCommands, Is.Empty);
    }

    private static BindingEntry MakeBinding(string id, string gestureType, int fingers, string direction,
        string actionType, int axis = 0, int button = 0, string mode = "hold")
    {
        var gesture = new JsonObject
        {
            ["type"] = gestureType,
            ["fingers"] = fingers,
            ["direction"] = direction
        };
        var action = new JsonObject { ["type"] = actionType, ["mode"] = mode };
        if (actionType == "axis") { action["axis"] = axis; action["sensitivity"] = 0.02f; }
        if (actionType == "button") { action["button"] = button; }
        return new BindingEntry { Id = id, Gesture = gesture, Action = action };
    }
}
