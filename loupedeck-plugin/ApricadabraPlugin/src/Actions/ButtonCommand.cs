using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class ButtonCommand : ActionEditorCommand
    {
        private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;

        private const string ModeControl = "btnMode";
        private const string ButtonControl = "btnButton";
        private const string DelayControl = "btnDelay";

        public ButtonCommand()
        {
            this.DisplayName = "vJoy Button";
            this.Description = "Map a button to a vJoy button";
            this.GroupName = "Apricadabra###Buttons";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ButtonControl, labelText: "Button")
                    .SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ModeControl, labelText: "Mode")
                    .SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: DelayControl, labelText: "Delay ms (Double Press)")
                    .SetValues(10, 200, 5, 50)
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
                for (int i = 1; i <= 128; i++)
                    e.AddItem(i.ToString(), $"Button {i}", null);
            }
        }

        protected override bool RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(ModeControl, out var mode)) return false;
            if (!actionParameters.TryGetString(ButtonControl, out var btnStr)) return false;
            if (!int.TryParse(btnStr, out var button)) return false;

            var msg = new JsonObject
            {
                ["type"] = "button",
                ["button"] = button,
                ["mode"] = mode,
            };

            switch (mode)
            {
                case "toggle":
                    msg["state"] = "down";
                    _ = Connection?.SendAsync(msg);
                    break;

                case "pulse":
                    _ = Connection?.SendAsync(msg);
                    break;

                case "double":
                    actionParameters.TryGetString(DelayControl, out var delayStr);
                    msg["delay"] = int.TryParse(delayStr, out var delay) ? delay : 50;
                    _ = Connection?.SendAsync(msg);
                    break;
            }

            return true;
        }
    }
}
