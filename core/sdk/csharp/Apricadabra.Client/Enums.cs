namespace Apricadabra.Client
{
    public enum AxisMode { Hold, Spring, Detent }
    public enum ButtonMode { Momentary, Toggle, Pulse, Double, Rapid, LongShort }
    public enum ButtonState { Down, Up }
    public enum ApiStatus { Exists, Deprecated, Undefined }

    public static class EnumExtensions
    {
        public static string ToWireString(this AxisMode mode) => mode switch
        {
            AxisMode.Hold => "hold",
            AxisMode.Spring => "spring",
            AxisMode.Detent => "detent",
            _ => "hold"
        };

        public static string ToWireString(this ButtonMode mode) => mode switch
        {
            ButtonMode.Momentary => "momentary",
            ButtonMode.Toggle => "toggle",
            ButtonMode.Pulse => "pulse",
            ButtonMode.Double => "double",
            ButtonMode.Rapid => "rapid",
            ButtonMode.LongShort => "longshort",
            _ => "momentary"
        };

        public static string ToWireString(this ButtonState state) => state switch
        {
            ButtonState.Down => "down",
            ButtonState.Up => "up",
            _ => "down"
        };

        public static ApiStatus ParseApiStatus(string wire) => wire switch
        {
            "exists" => ApiStatus.Exists,
            "deprecated" => ApiStatus.Deprecated,
            "undefined" => ApiStatus.Undefined,
            _ => ApiStatus.Undefined
        };
    }
}
