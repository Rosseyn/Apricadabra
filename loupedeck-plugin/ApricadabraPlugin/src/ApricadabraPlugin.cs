namespace Loupedeck.ApricadabraPlugin
{
    using System;
    using System.Collections.Generic;
    using Apricadabra.Client;

    public class ApricadabraPlugin : Plugin
    {
        public ApricadabraClient Connection { get; private set; }
        public StateDisplay State { get; private set; }
        private bool _wasConnected;

        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        public ApricadabraPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            this.State = new StateDisplay();
            this.Connection = new ApricadabraClient("loupedeck");

            this.Connection.OnConnected += (coreVersion, apiStatus) =>
            {
                PluginLog.Info($"Connected to core v{coreVersion}");
            };

            this.Connection.OnStateUpdate += (axes, buttons) =>
            {
                this.State.UpdateFromState(axes, buttons);
                if (!_wasConnected)
                {
                    _wasConnected = true;
                    PluginLog.Info("Connected to core");
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, "Connected", null, null);
                }
            };

            this.Connection.OnError += (code, message) =>
            {
                this.State.ErrorMessage = message;
                PluginLog.Error($"Core error: {code} - {message}");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error, message, null, null);
            };

            this.Connection.OnDisconnected += () =>
            {
                _wasConnected = false;
                this.State.ConnectionStatus = "Disconnected";
                PluginLog.Warning("Disconnected from core");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, "Disconnected from core", null, null);
            };

            _ = this.Connection.ConnectAsync();
        }

        public override void Unload()
        {
            this.Connection?.Dispose();
        }
    }
}
