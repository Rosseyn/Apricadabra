using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class ButtonCommand : ActionEditorCommand
    {
        private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;

        private const string ModeControl = "mode";
        private const string ButtonControl = "button";
        private const string DelayControl = "delay";
        private const string RateControl = "rate";
        private const string ShortButtonControl = "shortButton";
        private const string LongButtonControl = "longButton";
        private const string ThresholdControl = "threshold";
        private const string AxisControl = "axis";
        private const string ResetPositionControl = "resetPosition";

        public ButtonCommand()
        {
            this.DisplayName = "vJoy Button";
            this.Description = "Map a button to a vJoy button or axis reset";
            this.GroupName = "Apricadabra";

            this.ActionEditor.AddControl(
                new ActionEditorListbox(name: ModeControl, labelText: "Mode")
            );
            this.ActionEditor.AddControl(
                new ActionEditorListbox(name: ButtonControl, labelText: "Button")
            );
            // Double press delay
            this.ActionEditor.AddControl(
                new ActionEditorTextbox(name: DelayControl, labelText: "Delay ms (Double)")
            );
            // Rapid fire rate
            this.ActionEditor.AddControl(
                new ActionEditorTextbox(name: RateControl, labelText: "Rate ms (Rapid)")
            );
            // Long/Short buttons
            this.ActionEditor.AddControl(
                new ActionEditorListbox(name: ShortButtonControl, labelText: "Short Press Button")
            );
            this.ActionEditor.AddControl(
                new ActionEditorListbox(name: LongButtonControl, labelText: "Long Press Button")
            );
            this.ActionEditor.AddControl(
                new ActionEditorTextbox(name: ThresholdControl, labelText: "Hold Threshold ms")
            );
            // Reset Axis controls
            this.ActionEditor.AddControl(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis")
            );
            this.ActionEditor.AddControl(
                new ActionEditorTextbox(name: ResetPositionControl, labelText: "Reset Position (0-100)")
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
                e.AddItem("resetaxis", "Reset Axis", "Reset an axis to a position");
            }
            else if (e.ControlName == ButtonControl || e.ControlName == ShortButtonControl || e.ControlName == LongButtonControl)
            {
                for (int i = 1; i <= 128; i++)
                    e.AddItem(i.ToString(), $"Button {i}", null);
            }
            else if (e.ControlName == AxisControl)
            {
                e.AddItem("1", "X", null);
                e.AddItem("2", "Y", null);
                e.AddItem("3", "Z", null);
                e.AddItem("4", "Rx", null);
                e.AddItem("5", "Ry", null);
                e.AddItem("6", "Rz", null);
                e.AddItem("7", "Slider 1", null);
                e.AddItem("8", "Slider 2", null);
            }
        }

        protected override bool RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(ModeControl, out var mode)) return false;

            if (mode == "resetaxis")
            {
                return HandleResetAxis(actionParameters);
            }

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

        private bool HandleResetAxis(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return false;
            if (!int.TryParse(axisStr, out var axis)) return false;

            actionParameters.TryGetString(ResetPositionControl, out var resetStr);
            var resetPos = int.TryParse(resetStr, out var resetInt) ? resetInt / 100f : 0.5f;

            var msg = new JsonObject
            {
                ["type"] = "reset",
                ["axis"] = axis,
                ["position"] = resetPos,
            };
            _ = Connection?.SendAsync(msg);
            return true;
        }
    }
}
