using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class DialAction : ActionEditorAdjustment
    {
        private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;
        private StateDisplay StateDisplay => ((ApricadabraPlugin)this.Plugin).State;

        private const string AxisControl = "dialAxis";
        private const string ModeControl = "dialMode";
        private const string SensitivityControl = "dialSens";
        private const string InvertControl = "dialInv";
        private const string ButtonControl = "dialBtn";

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
                new ActionEditorListbox(name: ModeControl, labelText: "Mode")
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
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ButtonControl, labelText: "Encoder Press Button")
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
            else if (e.ControlName == ModeControl)
            {
                e.AddItem("hold", "Hold", "Maintains position");
                e.AddItem("spring", "Spring", "Returns to center");
                e.AddItem("detent", "Detent", "Discrete steps");
            }
            else if (e.ControlName == ButtonControl)
            {
                for (int i = 1; i <= 32; i++)
                    e.AddItem(i.ToString(), $"Button {i}", null);
            }
        }

        protected override bool ApplyAdjustment(ActionEditorActionParameters actionParameters, int diff)
        {
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return false;
            if (!int.TryParse(axisStr, out var axis)) return false;
            if (!actionParameters.TryGetString(ModeControl, out var mode)) return false;

            actionParameters.TryGetBoolean(InvertControl, out var invert);
            var adjustedDiff = invert ? -diff : diff;

            actionParameters.TryGetString(SensitivityControl, out var sensStr);
            var sensitivity = int.TryParse(sensStr, out var sensInt) ? sensInt / 1000f : 0.02f;

            var msg = new JsonObject
            {
                ["type"] = "axis",
                ["axis"] = axis,
                ["mode"] = mode,
                ["diff"] = adjustedDiff,
                ["sensitivity"] = sensitivity,
            };

            if (mode == "spring")
            {
                msg["decayRate"] = 0.95;
            }

            if (mode == "detent")
            {
                msg["steps"] = 5;
            }

            _ = Connection?.SendAsync(msg);
            return true;
        }

        protected override bool RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(ButtonControl, out var btnStr)) return true;
            if (!int.TryParse(btnStr, out var button)) return true;

            var msg = new JsonObject
            {
                ["type"] = "button",
                ["button"] = button,
                ["mode"] = "pulse",
            };
            _ = Connection?.SendAsync(msg);
            return true;
        }

        protected override string GetAdjustmentDisplayName(ActionEditorActionParameters actionParameters)
        {
            return "";
        }
    }
}
