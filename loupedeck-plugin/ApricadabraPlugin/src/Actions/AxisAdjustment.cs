using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class AxisAdjustment : ActionEditorAdjustment
    {
        private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;
        private StateDisplay StateDisplay => ((ApricadabraPlugin)this.Plugin).State;

        private const string ModeControl = "mode";
        private const string AxisControl = "axis";
        private const string InvertControl = "invert";
        private const string SensitivityControl = "sensitivity";
        private const string ResetPositionControl = "resetPosition";
        private const string DecayRateControl = "decayRate";
        private const string StepCountControl = "stepCount";

        public AxisAdjustment()
            : base(hasReset: true)
        {
            this.DisplayName = "vJoy Axis";
            this.Description = "Map a dial to a vJoy axis";
            this.GroupName = "Apricadabra";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ModeControl, labelText: "Mode")
                    .SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis")
                    .SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorCheckbox(name: InvertControl, labelText: "Invert")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: SensitivityControl, labelText: "Sensitivity")
                    .SetValues(1, 100, 1, 20)
                    .SetFormatString("{0}%")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: ResetPositionControl, labelText: "Reset Position")
                    .SetValues(0, 100, 1, 50)
                    .SetFormatString("{0}%")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: DecayRateControl, labelText: "Decay Rate")
                    .SetValues(0, 100, 1, 95)
                    .SetFormatString("{0}%")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: StepCountControl, labelText: "Steps")
                    .SetValues(2, 20, 1, 5)
            );

            this.ActionEditor.ListboxItemsRequested += OnListboxItemsRequested;
        }

        private void OnListboxItemsRequested(object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName == ModeControl)
            {
                e.AddItem("hold", "Hold", "Maintains position");
                e.AddItem("spring", "Spring", "Returns to center");
                e.AddItem("detent", "Detent", "Discrete steps");
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

        protected override bool ApplyAdjustment(ActionEditorActionParameters actionParameters, int diff)
        {
            if (!actionParameters.TryGetString(ModeControl, out var mode)) return false;
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return false;
            if (!int.TryParse(axisStr, out var axis)) return false;

            actionParameters.TryGetBoolean(InvertControl, out var invert);
            var adjustedDiff = invert ? -diff : diff;

            var msg = new JsonObject
            {
                ["type"] = "axis",
                ["axis"] = axis,
                ["mode"] = mode,
                ["diff"] = adjustedDiff,
            };

            if (mode == "hold" || mode == "spring")
            {
                actionParameters.TryGetString(SensitivityControl, out var sensStr);
                var sensitivity = int.TryParse(sensStr, out var sensInt) ? sensInt / 1000f : 0.02f;
                msg["sensitivity"] = sensitivity;
            }

            if (mode == "spring")
            {
                actionParameters.TryGetString(DecayRateControl, out var decayStr);
                var decay = int.TryParse(decayStr, out var decayInt) ? decayInt / 100f : 0.95f;
                msg["decayRate"] = decay;
            }

            if (mode == "detent")
            {
                actionParameters.TryGetString(StepCountControl, out var stepsStr);
                var steps = int.TryParse(stepsStr, out var stepsInt) ? stepsInt : 5;
                msg["steps"] = steps;
            }

            _ = Connection?.SendAsync(msg);
            this.AdjustmentValueChanged();
            return true;
        }

        protected override bool RunCommand(ActionEditorActionParameters actionParameters)
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
            this.AdjustmentValueChanged();
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
