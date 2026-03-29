using System;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Apricadabra.Trackpad.Core;
using Apricadabra.Trackpad.Core.Bindings;

namespace Apricadabra.Trackpad.ViewModels
{
    public class BindingRowViewModel : ViewModelBase
    {
        public BindingEntry Entry { get; set; }
        private bool _isEditing;

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public string GestureDisplay => Entry == null ? "" :
            $"{(Entry.GestureFingers > 0 ? Entry.GestureFingers + "-finger " : "")}" +
            $"{Entry.GestureType} {Entry.GestureDirection}".Trim();

        public string ActionDisplay => Entry == null ? "" :
            Entry.ActionType == "axis"
                ? $"Axis {Entry.ActionAxis} ({Entry.ActionMode})"
                : $"Button {Entry.ActionButton} ({Entry.ActionMode})";

        // Edit fields
        public string EditGestureType { get; set; } = "scroll";
        public int EditFingers { get; set; } = 2;
        public string EditDirection { get; set; } = "up";
        public string EditActionType { get; set; } = "axis";
        public int EditAxis { get; set; } = 1;
        public int EditButton { get; set; } = 1;
        public string EditMode { get; set; } = "hold";
        public float EditSensitivity { get; set; } = 0.02f;
        public float EditDecayRate { get; set; } = 0.95f;
        public int EditSteps { get; set; } = 5;

        public void LoadFromEntry()
        {
            if (Entry == null) return;
            EditGestureType = Entry.GestureType ?? "scroll";
            EditFingers = Entry.GestureFingers > 0 ? Entry.GestureFingers : 2;
            EditDirection = Entry.GestureDirection ?? "up";
            EditActionType = Entry.ActionType ?? "axis";
            EditAxis = Entry.ActionAxis;
            EditButton = Entry.ActionButton;
            EditMode = Entry.ActionMode ?? "hold";
            EditSensitivity = Entry.ActionSensitivity;
            EditDecayRate = Entry.ActionDecayRate;
            EditSteps = Entry.ActionSteps;
        }

        public BindingEntry ToEntry()
        {
            var gesture = new JsonObject
            {
                ["type"] = EditGestureType,
                ["fingers"] = EditFingers,
                ["direction"] = EditDirection
            };
            var action = new JsonObject { ["type"] = EditActionType, ["mode"] = EditMode };
            if (EditActionType == "axis")
            {
                action["axis"] = EditAxis;
                action["sensitivity"] = EditSensitivity;
                if (EditMode == "spring") action["decayRate"] = EditDecayRate;
                if (EditMode == "detent") action["steps"] = EditSteps;
            }
            else
            {
                action["button"] = EditButton;
            }

            return new BindingEntry
            {
                Id = Entry?.Id ?? $"{EditGestureType}-{EditFingers}-{EditDirection}-{Guid.NewGuid():N}".Substring(0, 32),
                Gesture = gesture,
                Action = action
            };
        }
    }

    public class BindingsViewModel : ViewModelBase
    {
        private readonly TrackpadService _service;
        public ObservableCollection<BindingRowViewModel> Rows { get; } = new();

        public ICommand AddCommand { get; }
        public ICommand SaveEditCommand { get; }
        public ICommand CancelEditCommand { get; }

        private BindingRowViewModel _editingRow;

        public BindingsViewModel(TrackpadService service)
        {
            _service = service;
            LoadRows();

            AddCommand = new RelayCommand(AddBinding);
            SaveEditCommand = new RelayCommand(SaveEdit);
            CancelEditCommand = new RelayCommand(CancelEdit);
        }

        private void LoadRows()
        {
            Rows.Clear();
            foreach (var entry in _service.BindingConfig.Bindings)
            {
                Rows.Add(new BindingRowViewModel { Entry = entry });
            }
        }

        public void StartEdit(BindingRowViewModel row)
        {
            CancelEdit();
            row.LoadFromEntry();
            row.IsEditing = true;
            _editingRow = row;
        }

        public void DeleteBinding(BindingRowViewModel row)
        {
            _service.BindingConfig.Bindings.Remove(row.Entry);
            _service.BindingConfig.Save();
            Rows.Remove(row);
        }

        private void AddBinding()
        {
            CancelEdit();
            var row = new BindingRowViewModel { IsEditing = true };
            Rows.Add(row);
            _editingRow = row;
        }

        private void SaveEdit()
        {
            if (_editingRow == null) return;
            var newEntry = _editingRow.ToEntry();

            if (_editingRow.Entry != null)
            {
                // Editing existing
                var idx = _service.BindingConfig.Bindings.IndexOf(_editingRow.Entry);
                if (idx >= 0) _service.BindingConfig.Bindings[idx] = newEntry;
            }
            else
            {
                // Adding new
                _service.BindingConfig.Bindings.Add(newEntry);
            }

            _editingRow.Entry = newEntry;
            _editingRow.IsEditing = false;
            _editingRow.OnPropertyChanged(nameof(BindingRowViewModel.GestureDisplay));
            _editingRow.OnPropertyChanged(nameof(BindingRowViewModel.ActionDisplay));
            _editingRow = null;
            _service.BindingConfig.Save();
        }

        private void CancelEdit()
        {
            if (_editingRow == null) return;
            if (_editingRow.Entry == null)
                Rows.Remove(_editingRow); // was a new row, remove it
            else
                _editingRow.IsEditing = false;
            _editingRow = null;
        }
    }
}
