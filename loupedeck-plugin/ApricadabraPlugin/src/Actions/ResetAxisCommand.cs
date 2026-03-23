using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class ResetAxisCommand : ActionEditorCommand
    {
        private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;

        private const string AxisControl = "rstAxis";
        private const string PositionControl = "rstPosition";

        public ResetAxisCommand()
        {
            this.DisplayName = "vJoy Reset Axis";
            this.Description = "Reset a vJoy axis to a configured position";
            this.GroupName = "Apricadabra###Reset";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis")
                    .SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: PositionControl, labelText: "Reset Position")
                    .SetValues(0, 100, 1, 50)
                    .SetFormatString("{0}%")
            );

            this.ActionEditor.ListboxItemsRequested += OnListboxItemsRequested;
        }

        private void OnListboxItemsRequested(object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName == AxisControl)
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
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return false;
            if (!int.TryParse(axisStr, out var axis)) return false;

            // Debug: log raw values from all getter methods
            actionParameters.TryGetString(PositionControl, out var posStr);
            actionParameters.TryGetInt32(PositionControl, out var posInt);
            PluginLog.Info($"Position raw: string='{posStr}' int={posInt}");

            var position = posInt > 0 ? posInt / 100f : (int.TryParse(posStr, out var parsed) ? parsed / 100f : 0.5f);

            var msg = new JsonObject
            {
                ["type"] = "reset",
                ["axis"] = axis,
                ["position"] = position,
            };
            PluginLog.Info($"Sending reset: axis={axis} position={position}");
            _ = Connection?.SendAsync(msg);
            return true;
        }
    }
}
