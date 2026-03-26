using System;
using System.Diagnostics;
using System.IO;

namespace Apricadabra.Client
{
    internal class CoreLauncher
    {
        private const string CoreExeName = "apricadabra-core.exe";
        private readonly string[] _additionalPaths;
        private DateTime _suppressUntil = DateTime.MinValue;

        public CoreLauncher(string[] additionalPaths)
        {
            _additionalPaths = additionalPaths ?? Array.Empty<string>();
        }

        public void SuppressLaunchUntil(DateTime until)
        {
            _suppressUntil = until;
        }

        public void TryLaunch()
        {
            if (DateTime.UtcNow < _suppressUntil)
                return;

            try
            {
                var corePath = FindCore();
                if (corePath == null) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = corePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Apricadabra] Failed to launch core: {ex.Message}");
            }
        }

        private string FindCore()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var primaryPath = Path.Combine(appData, "Apricadabra", CoreExeName);
            if (File.Exists(primaryPath))
                return primaryPath;

            foreach (var dir in _additionalPaths)
            {
                var path = Path.Combine(dir, CoreExeName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }
    }
}
