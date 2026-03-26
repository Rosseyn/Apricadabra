using NUnit.Framework;
using Apricadabra.Trackpad.Core.Gestures;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Models;
using System;
using System.Collections.Generic;

namespace Apricadabra.Trackpad.Core.Tests;

[TestFixture]
public class GestureRecognizerTests
{
    private GestureRecognizer _recognizer;
    private TrackpadSettings _settings;
    private List<GestureEvent> _events;

    [SetUp]
    public void Setup()
    {
        _settings = new TrackpadSettings();
        _recognizer = new GestureRecognizer(_settings);
        _events = new List<GestureEvent>();
        _recognizer.OnGestureEvent += e => _events.Add(e);
    }

    private ContactFrame Frame(DateTime t, params ContactPoint[] contacts)
        => new ContactFrame(contacts, t);

    [Test]
    public void TwoFingerLinearMotion_DefaultSettings_IsScroll()
    {
        var t = DateTime.UtcNow;
        // Two fingers moving up
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.4f, 0.4f, true),
            new ContactPoint(2, 0.6f, 0.4f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(32),
            new ContactPoint(1, 0.4f, 0.3f, true),
            new ContactPoint(2, 0.6f, 0.3f, true)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Scroll && e.Direction == GestureDirection.Up));
    }

    [Test]
    public void ThreeFingerLinearMotion_DefaultSettings_IsSwipe()
    {
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.5f, 0.5f, true),
            new ContactPoint(3, 0.7f, 0.5f, true)));
        // Move left
        for (int i = 1; i <= 10; i++)
        {
            float offset = i * 0.03f;
            _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16 * i),
                new ContactPoint(1, 0.3f - offset, 0.5f, true),
                new ContactPoint(2, 0.5f - offset, 0.5f, true),
                new ContactPoint(3, 0.7f - offset, 0.5f, true)));
        }
        // Lift
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(200),
            new ContactPoint(1, 0f, 0.5f, false),
            new ContactPoint(2, 0.2f, 0.5f, false),
            new ContactPoint(3, 0.4f, 0.5f, false)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Swipe &&
            e.Direction == GestureDirection.Left &&
            e.Fingers == 3 &&
            e.Phase == GesturePhase.End));
    }

    [Test]
    public void TwoFingerSpread_IsPinch()
    {
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.45f, 0.5f, true),
            new ContactPoint(2, 0.55f, 0.5f, true)));
        // Spread apart significantly
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.7f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(32),
            new ContactPoint(1, 0.2f, 0.5f, true),
            new ContactPoint(2, 0.8f, 0.5f, true)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Pinch && e.Direction == GestureDirection.Out));
    }

    [Test]
    public void TwoFingerQuickTapAndRelease_IsTap()
    {
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)));
        // Lift within tap duration, minimal movement
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(100),
            new ContactPoint(1, 0.4f, 0.5f, false),
            new ContactPoint(2, 0.6f, 0.5f, false)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Tap && e.Fingers == 2));
    }

    [Test]
    public void GestureCommitment_NoReclassification()
    {
        var t = DateTime.UtcNow;
        // Start as scroll
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.4f, 0.4f, true),
            new ContactPoint(2, 0.6f, 0.4f, true)));

        // Even if fingers now spread, should stay as scroll (committed)
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(32),
            new ContactPoint(1, 0.3f, 0.3f, true),
            new ContactPoint(2, 0.7f, 0.3f, true)));

        var scrollEvents = _events.FindAll(e => e.Type == GestureType.Scroll);
        var pinchEvents = _events.FindAll(e => e.Type == GestureType.Pinch);
        Assert.That(scrollEvents.Count, Is.GreaterThan(0));
        Assert.That(pinchEvents.Count, Is.EqualTo(0));
    }

    [Test]
    public void CustomScrollFingerCount_ThreeFingerScroll()
    {
        _settings.ScrollFingerCount = 3;
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.5f, 0.5f, true),
            new ContactPoint(3, 0.7f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.3f, 0.4f, true),
            new ContactPoint(2, 0.5f, 0.4f, true),
            new ContactPoint(3, 0.7f, 0.4f, true)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Scroll && e.Fingers == 3));
    }
}
