namespace Apricadabra.Trackpad.Core.Input
{
    public class TouchpadDevice
    {
        public string DevicePath { get; }
        public string Name { get; }
        public int MaxContacts { get; }

        public TouchpadDevice(string devicePath, string name, int maxContacts)
        {
            DevicePath = devicePath;
            Name = name;
            MaxContacts = maxContacts;
        }

        public override string ToString() => $"{Name} ({MaxContacts} contacts)";
    }
}
