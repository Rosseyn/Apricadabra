using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class AxisButtonAdjustment : ActionEditorAdjustment
    {
        private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;
        private StateDisplay StateDisplay => ((ApricadabraPlugin)this.Plugin).State;

        private const string AxisControl = "axis";
        private const string InvertControl = "invert";
        private const string SensitivityControl = "sensitivity";
        private const string ButtonControl = "button";

        public AxisButtonAdjustment()
            : base(hasReset: true)
        {
            this.DisplayName = "vJoy Axis + Button";
            this.Description = "Dial controls axis, encoder press fires button";
            this.GroupName = "Apricadabra";

            this.ActionEditor.AddControl(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis")
            );
            this.ActionEditor.AddControl(
                new ActionEditorCheckbox(name: InvertControl, labelText: "Invert")
            );
            this.ActionEditor.AddControl(
                new ActionEditorTextbox(name: SensitivityControl, labelText: "Sensitivity (1-100)")
            );
            this.ActionEditor.AddControl(
                new ActionEditorListbox(name: ButtonControl, labelText: "Button")
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
            else if (e.ControlName == ButtonControl)
            {
                for (int i = 1; i <= 128; i++)
                    e.AddItem(i.ToString(), $"Button {i}", null);
            }
        }

        protected override bool ApplyAdjustment(ActionEditorActionParameters actionParameters, int diff)
        {
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return false;
            if (!int.TryParse(axisStr, out var axis)) return false;

            actionParameters.TryGetBoolean(InvertControl, out var invert);
            actionParameters.TryGetString(SensitivityControl, out var sensStr);
            var sensitivity = int.TryParse(sensStr, out var sensInt) ? sensInt / 1000f : 0.02f;

            var msg = new JsonObject
            {
                ["type"] = "axis",
                ["axis"] = axis,
                ["mode"] = "hold",
                ["diff"] = invert ? -diff : diff,
                ["sensitivity"] = sensitivity,
            };

            _ = Connection?.SendAsync(msg);
            this.AdjustmentValueChanged();
            return true;
        }

        protected override bool RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(ButtonControl, out var btnStr)) return false;
            if (!int.TryParse(btnStr, out var button)) return false;

            var downMsg = new JsonObject
            {
                ["type"] = "button",
                ["button"] = button,
                ["mode"] = "pulse",
            };
            _ = Connection?.SendAsync(downMsg);
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
