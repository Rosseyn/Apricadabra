using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Apricadabra.Trackpad.Core.Models;

namespace Apricadabra.Trackpad.Core.Input
{
    public class RawInputCapture : IDisposable
    {
        private readonly HidTouchpadParser _parser;
        private IntPtr _hwnd;
        private Thread _messageThread;
        private WndProc _wndProcDelegate; // prevent GC collection
        private string _selectedDevicePath;
        private volatile bool _running;
        private readonly ManualResetEventSlim _windowReady = new ManualResetEventSlim(false);
        private string _windowClassName;

        public event Action<ContactFrame> OnContactFrame;
        public event Action OnDevicesChanged;
        public IReadOnlyList<TouchpadDevice> AvailableDevices => _parser.Devices;

        public string SelectedDevicePath
        {
            get => _selectedDevicePath;
            set => _selectedDevicePath = value;
        }

        public RawInputCapture()
        {
            _parser = new HidTouchpadParser();
            _parser.OnContactFrame += frame => OnContactFrame?.Invoke(frame);
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;
            _windowReady.Reset();

            _messageThread = new Thread(MessageLoop)
            {
                Name = "RawInputCapture",
                IsBackground = true
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();

            // Wait for the window to be created before returning
            _windowReady.Wait(TimeSpan.FromSeconds(5));
        }

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;

            // Post WM_CLOSE to the message-only window. DefWindowProc handles
            // WM_CLOSE by calling DestroyWindow, which posts WM_DESTROY.
            // Our WndProc handles WM_DESTROY by calling PostQuitMessage(0),
            // which causes GetMessage to return 0 and exit the loop.
            if (_hwnd != IntPtr.Zero)
                NativeMethods.PostMessage(_hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

            if (_messageThread != null && _messageThread.IsAlive)
                _messageThread.Join(TimeSpan.FromSeconds(3));

            _hwnd = IntPtr.Zero;
            _messageThread = null;
        }

        private void MessageLoop()
        {
            _windowClassName = "ApricadabraRawInput_" + Guid.NewGuid().ToString("N");

            // Must hold a reference to prevent GC
            _wndProcDelegate = WndProc;

            IntPtr hInstance = NativeMethods.GetModuleHandle(null);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = hInstance,
                lpszClassName = _windowClassName,
            };

            ushort atom = NativeMethods.RegisterClassEx(ref wc);
            if (atom == 0)
            {
                _running = false;
                _windowReady.Set();
                return;
            }

            _hwnd = NativeMethods.CreateWindowEx(
                0,
                _windowClassName,
                "ApricadabraRawInput",
                0,
                0, 0, 0, 0,
                RawInputConstants.HWND_MESSAGE,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                _running = false;
                _windowReady.Set();
                return;
            }

            // Enumerate devices before registering
            _parser.EnumerateDevices();

            // Register for raw input: precision touchpad
            var rid = new RAWINPUTDEVICE[]
            {
                new RAWINPUTDEVICE
                {
                    UsagePage = RawInputConstants.HID_USAGE_PAGE_DIGITIZER,
                    Usage = RawInputConstants.HID_USAGE_DIGITIZER_TOUCH_PAD,
                    Flags = RawInputConstants.RIDEV_INPUTSINK | RawInputConstants.RIDEV_DEVNOTIFY,
                    WindowHandle = _hwnd,
                }
            };

            bool registered = NativeMethods.RegisterRawInputDevices(
                rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            if (!registered)
            {
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
                _running = false;
                _windowReady.Set();
                return;
            }

            _windowReady.Set();

            // Message pump
            while (_running)
            {
                int ret = NativeMethods.GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
                if (ret == 0 || ret == -1)
                    break;

                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }

            // Window was already destroyed via WM_CLOSE -> DestroyWindow
            _hwnd = IntPtr.Zero;
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == RawInputConstants.WM_INPUT)
            {
                HandleRawInput(lParam);
                return IntPtr.Zero;
            }

            if (msg == RawInputConstants.WM_INPUT_DEVICE_CHANGE)
            {
                _parser.EnumerateDevices();
                OnDevicesChanged?.Invoke();
                return IntPtr.Zero;
            }

            const uint WM_DESTROY = 0x0002;
            if (msg == WM_DESTROY)
            {
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void HandleRawInput(IntPtr lParam)
        {
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

            // Step 1: Get required buffer size
            uint size = 0;
            uint sizeResult = NativeMethods.GetRawInputData(
                lParam, RawInputConstants.RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0)
                return;

            // Step 2: Allocate and fill buffer
            byte[] buffer = new byte[size];
            uint dataResult = NativeMethods.GetRawInputData(
                lParam, RawInputConstants.RID_INPUT, buffer, ref size, headerSize);
            if (dataResult == unchecked((uint)-1))
                return;

            // Step 3: Read header
            GCHandle bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            RAWINPUTHEADER header;
            try
            {
                header = Marshal.PtrToStructure<RAWINPUTHEADER>(bufferHandle.AddrOfPinnedObject());
            }
            finally
            {
                bufferHandle.Free();
            }

            // Step 4: Must be HID type
            if (header.Type != RawInputConstants.RIM_TYPEHID)
                return;

            // Step 5: Device filtering
            if (_selectedDevicePath != null)
            {
                if (!_parser.DeviceContexts.TryGetValue(header.Device, out var ctx))
                    return;
                if (!string.Equals(ctx.DevicePath, _selectedDevicePath, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            else
            {
                // If no device selected, still must be a known device
                if (!_parser.DeviceContexts.ContainsKey(header.Device))
                    return;
            }

            // Step 6: Extract HID data
            // After RAWINPUTHEADER, the RAWHID structure is:
            //   dwSizeHid (4 bytes) + dwCount (4 bytes) + bRawData[dwSizeHid * dwCount]
            int hidDataOffset = (int)headerSize;
            int hidDataLength = (int)size - hidDataOffset;

            if (hidDataLength <= 8)
                return;

            byte[] hidData = new byte[hidDataLength];
            Array.Copy(buffer, hidDataOffset, hidData, 0, hidDataLength);

            // Step 7: Pass to parser
            _parser.ProcessRawInput(header.Device, hidData, hidDataLength);
        }

        public void Dispose()
        {
            Stop();
            _parser.Dispose();
            _windowReady.Dispose();
        }
    }
}
