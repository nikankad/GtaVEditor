using System.ComponentModel;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>One value in the Save data grid (any field of the save's PSO tree). Edits are type-checked before writing.</summary>
public sealed class PsoValueRow(PsoNode node, Action onChanged) : INotifyPropertyChanged, IDataErrorInfo
{
    private string? _error;
    private string? _pending;

    public PsoNode Node => node;
    public string Path => node.Path;
    public string Type => node.TypeName;
    public string Location => $"PSIN+0x{node.Offset:X}";
    public string Notes => _error ?? "";

    public string Value
    {
        get => _pending ?? node.ValueText ?? "";
        set
        {
            _error = null;
            _pending = null;
            if (value != node.ValueText)
            {
                try
                {
                    node.SetValue(value);
                    onChanged();
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    _error = $"Not a valid {node.TypeName}: {ex.Message}";
                    _pending = value;
                }
            }
            Refresh();
        }
    }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new(nameof(Value)));
        PropertyChanged?.Invoke(this, new(nameof(Notes)));
    }

    public string Error => _error ?? "";
    public string this[string column] => column == nameof(Value) ? _error ?? "" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
}
