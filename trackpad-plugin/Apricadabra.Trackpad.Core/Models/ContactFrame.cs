using System;

namespace Apricadabra.Trackpad.Core.Models
{
    public class ContactFrame
    {
        public ContactPoint[] Contacts { get; }
        public DateTime Timestamp { get; }

        public ContactFrame(ContactPoint[] contacts, DateTime timestamp)
        {
            Contacts = contacts;
            Timestamp = timestamp;
        }
    }

    public struct ContactPoint
    {
        public int Id;
        public float X;
        public float Y;
        public bool OnSurface;

        public ContactPoint(int id, float x, float y, bool onSurface)
        {
            Id = id;
            X = x;
            Y = y;
            OnSurface = onSurface;
        }
    }
}
