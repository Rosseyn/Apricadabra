using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class DialAction : ActionEditorAdjustment
    {
        private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;
        private StateDisplay StateDisplay => ((ApricadabraPlugin)this.Plugin).State;

        private const string AxisControl = "dialAxis";
        private const string SensitivityControl = "dialSensitivity";
        private const string InvertControl = "dialInvert";

        public DialAction()
            : base(hasReset: false)
        {
            this.DisplayName = "vJoy Dial";
            this.Description = "Map a dial to a vJoy axis";
            this.GroupName = "Apricadabra";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis")
                    .SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: SensitivityControl, labelText: "Sensitivity")
                    .SetValues(1, 100, 1, 20)
                    .SetFormatString("{0}%")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorCheckbox(name: InvertControl, labelText: "Invert")
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

        protected override bool ApplyAdjustment(ActionEditorActionParameters actionParameters, int diff)
        {
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return false;
            if (!int.TryParse(axisStr, out var axis)) return false;

            actionParameters.TryGetBoolean(InvertControl, out var invert);
            var adjustedDiff = invert ? -diff : diff;

            actionParameters.TryGetString(SensitivityControl, out var sensStr);
            var sensitivity = int.TryParse(sensStr, out var sensInt) ? sensInt / 1000f : 0.02f;

            var msg = new JsonObject
            {
                ["type"] = "axis",
                ["axis"] = axis,
                ["mode"] = "hold",
                ["diff"] = adjustedDiff,
                ["sensitivity"] = sensitivity,
            };

            _ = Connection?.SendAsync(msg);
            return true;
        }

        protected override bool RunCommand(ActionEditorActionParameters actionParameters)
        {
            // Encoder press — no action for now
            return true;
        }

        protected override string GetAdjustmentDisplayName(ActionEditorActionParameters actionParameters)
        {
            if (StateDisplay == null) return "---";
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return "---";
            if (!int.TryParse(axisStr, out var axis)) return "---";
            return StateDisplay.GetAxisDisplayString(axis);
        }
    }
}
