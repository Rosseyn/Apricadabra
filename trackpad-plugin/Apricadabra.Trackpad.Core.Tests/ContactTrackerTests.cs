using NUnit.Framework;
using Apricadabra.Trackpad.Core.Gestures;
using Apricadabra.Trackpad.Core.Models;
using System;

namespace Apricadabra.Trackpad.Core.Tests;

[TestFixture]
public class ContactTrackerTests
{
    private ContactTracker _tracker;

    [SetUp]
    public void Setup() => _tracker = new ContactTracker();

    [Test]
    public void Update_SingleContact_TracksDelta()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, true)
        }, t));

        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.6f, 0.5f, true)
        }, t.AddMilliseconds(16)));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(1));
        Assert.That(state.CenterDeltaX, Is.EqualTo(0.1f).Within(0.01f));
        Assert.That(state.CenterDeltaY, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void Update_TwoContacts_ComputesSpread()
    {
        var t = DateTime.UtcNow;
        // Two fingers 0.2 apart
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)
        }, t));

        // Spread to 0.4 apart
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.7f, 0.5f, true)
        }, t.AddMilliseconds(16)));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(2));
        Assert.That(state.SpreadDelta, Is.GreaterThan(0)); // fingers moved apart
    }

    [Test]
    public void Update_TwoContacts_ComputesRotation()
    {
        var t = DateTime.UtcNow;
        // Two fingers horizontal
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)
        }, t));

        // Rotate: finger 1 goes up, finger 2 goes down
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.4f, 0.4f, true),
            new ContactPoint(2, 0.6f, 0.6f, true)
        }, t.AddMilliseconds(16)));

        var state = _tracker.CurrentState;
        Assert.That(state.RotationDelta, Is.Not.EqualTo(0).Within(0.001f));
    }

    [Test]
    public void Update_ContactLifted_DetectsFingerUp()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, true)
        }, t));

        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, false)
        }, t.AddMilliseconds(100)));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(0));
        Assert.That(state.AllFingersLifted, Is.True);
    }

    [Test]
    public void Update_NoContacts_ReturnsEmptyState()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(Array.Empty<ContactPoint>(), t));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(0));
    }

    [Test]
    public void CumulativeDistance_TracksTotal()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, true)
        }, t));
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.6f, 0.5f, true)
        }, t.AddMilliseconds(16)));
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.7f, 0.5f, true)
        }, t.AddMilliseconds(32)));

        Assert.That(_tracker.CurrentState.CumulativeDistance, Is.EqualTo(0.2f).Within(0.01f));
    }
}
