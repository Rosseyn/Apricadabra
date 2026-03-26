using System;
using Apricadabra.Client;

namespace Loupedeck.ApricadabraPlugin
{
    public class ButtonCommand : ActionEditorCommand
    {
        private ApricadabraClient Connection => ((ApricadabraPlugin)this.Plugin).Connection;

        private const string ButtonControl = "btnId";
        private const string ModeControl = "btnMode";

        public ButtonCommand()
        {
            this.DisplayName = "vJoy Button";
            this.Description = "Fire a vJoy button";
            this.GroupName = "Apricadabra";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ButtonControl, labelText: "Button")
                    .SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ModeControl, labelText: "Mode")
                    .SetRequired()
            );

            this.ActionEditor.ListboxItemsRequested += OnListboxItemsRequested;
        }

        private void OnListboxItemsRequested(object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName == ModeControl)
            {
                e.AddItem("pulse", "Pulse", "Brief press/release");
                e.AddItem("toggle", "Toggle", "On/off on each press");
                e.AddItem("double", "Double Press", "Two rapid pulses");
            }
            else if (e.ControlName == ButtonControl)
            {
                for (int i = 1; i <= 32; i++)
                    e.AddItem(i.ToString(), $"Button {i}", null);
            }
        }

        private static (ButtonMode mode, ButtonState? state) ParseButtonMode(string mode) => mode switch
        {
            "toggle" => (ButtonMode.Toggle, ButtonState.Down),
            "double" => (ButtonMode.Double, null),
            _ => (ButtonMode.Pulse, null)
        };

        protected override bool RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(ButtonControl, out var btnStr)) return false;
            if (!int.TryParse(btnStr, out var button)) return false;
            if (!actionParameters.TryGetString(ModeControl, out var modeStr)) return false;

            var (buttonMode, buttonState) = ParseButtonMode(modeStr);

            PluginLog.Info($"Sending button: btn={button} mode={modeStr}");
            Connection?.SendButton(button, buttonMode, buttonState);
            return true;
        }
    }
}
