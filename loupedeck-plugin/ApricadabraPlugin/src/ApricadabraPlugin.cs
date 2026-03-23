namespace Loupedeck.ApricadabraPlugin
{
    using System;

    public class ApricadabraPlugin : Plugin
    {
        public CoreConnection Connection { get; private set; }
        public StateDisplay State { get; private set; }

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
            this.Connection = new CoreConnection();

            this.Connection.OnStateUpdate += msg =>
            {
                this.State.UpdateFromState(msg);
                PluginLog.Info("Connected to core, received state update");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, "Connected", null, null);
            };

            this.Connection.OnError += (code, message) =>
            {
                this.State.ErrorMessage = message;
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error, message, null, null);
            };

            this.Connection.OnDisconnected += () =>
            {
                this.State.ConnectionStatus = "Disconnected";
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, "Disconnected from core", null, null);
            };

            this.Connection.OnShutdown += () =>
            {
                this.State.ConnectionStatus = "Core shutting down";
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, "Core shutting down", null, null);
            };

            _ = this.Connection.ConnectAsync();
        }

        public override void Unload()
        {
            this.Connection?.Dispose();
        }
    }
}
