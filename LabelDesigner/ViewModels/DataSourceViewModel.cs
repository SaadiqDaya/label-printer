using LabelDesigner.Core.Models;

namespace LabelDesigner.ViewModels;

public class DataSourceViewModel : ViewModelBase
{
    private readonly DataSourceDefinition _model;

    public Guid Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set { if (_model.Name != value) { _model.Name = value; OnPropertyChanged(); } }
    }

    public DataSourceType Type
    {
        get => _model.Type;
        set
        {
            if (_model.Type == value) return;
            _model.Type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSerial));
            OnPropertyChanged(nameof(IsFormula));
            // Reset format to a sensible default for the new type
            if (string.IsNullOrWhiteSpace(Format) || IsDefaultFormat(Format))
                Format = DefaultFormat(value);
        }
    }

    public string Format
    {
        get => _model.Format;
        set { if (_model.Format != value) { _model.Format = value; OnPropertyChanged(); } }
    }

    public int RelativeMonths
    {
        get => _model.RelativeMonths;
        set { if (_model.RelativeMonths != value) { _model.RelativeMonths = value; OnPropertyChanged(); } }
    }

    public int RelativeDays
    {
        get => _model.RelativeDays;
        set { if (_model.RelativeDays != value) { _model.RelativeDays = value; OnPropertyChanged(); } }
    }

    public int SerialStart
    {
        get => _model.SerialStart;
        set { if (_model.SerialStart != value) { _model.SerialStart = value; OnPropertyChanged(); } }
    }

    public int Increment
    {
        get => _model.Increment;
        set { if (_model.Increment != value) { _model.Increment = value; OnPropertyChanged(); } }
    }

    public SerialMode SerialMode
    {
        get => _model.SerialMode;
        set { if (_model.SerialMode != value) { _model.SerialMode = value; OnPropertyChanged(); } }
    }

    public string SerialPrefix
    {
        get => _model.SerialPrefix;
        set { if (_model.SerialPrefix != value) { _model.SerialPrefix = value; OnPropertyChanged(); } }
    }

    public string SerialSuffix
    {
        get => _model.SerialSuffix;
        set { if (_model.SerialSuffix != value) { _model.SerialSuffix = value; OnPropertyChanged(); } }
    }

    /// <summary>10 = decimal (uses Format), 36 = alphanumeric 0-9A-Z (uses pad width).</summary>
    public int SerialRadix
    {
        get => _model.SerialRadix;
        set { if (_model.SerialRadix != value) { _model.SerialRadix = value; OnPropertyChanged(); } }
    }

    public int SerialPadWidth
    {
        get => _model.SerialPadWidth;
        set { if (_model.SerialPadWidth != value) { _model.SerialPadWidth = value; OnPropertyChanged(); } }
    }

    /// <summary>UI convenience: true = base-36 alphanumeric serials (0-9 A-Z), false = decimal.</summary>
    public bool SerialAlphanumeric
    {
        get => _model.SerialRadix == 36;
        set { _model.SerialRadix = value ? 36 : 10; OnPropertyChanged(); OnPropertyChanged(nameof(SerialRadix)); }
    }

    /// <summary>True when this source is a Serial — used to show/hide serial-only editor fields.</summary>
    public bool IsSerial => _model.Type == DataSourceType.Serial;

    public string FixedValue
    {
        get => _model.FixedValue;
        set { if (_model.FixedValue != value) { _model.FixedValue = value; OnPropertyChanged(); } }
    }

    public string FormulaExpression
    {
        get => _model.FormulaExpression;
        set { if (_model.FormulaExpression != value) { _model.FormulaExpression = value; OnPropertyChanged(); } }
    }

    /// <summary>True when this source is a Formula — used to show/hide the expression editor.</summary>
    public bool IsFormula => _model.Type == DataSourceType.Formula;

    public string DisplayName => $"{Name} ({Type})";

    public DataSourceDefinition ToModel() => _model;

    public DataSourceViewModel(DataSourceDefinition model) => _model = model;

    public DataSourceViewModel() : this(new DataSourceDefinition()) { }

    private static string DefaultFormat(DataSourceType t) => t switch
    {
        DataSourceType.CurrentTime  => "HH:mm",
        DataSourceType.Serial       => "D4",
        DataSourceType.FixedValue   => "",
        _                           => "dd/MM/yyyy"
    };

    private static bool IsDefaultFormat(string f) =>
        f is "dd/MM/yyyy" or "HH:mm" or "D4" or "";

    /// <summary>All available DataSourceType values for ComboBox binding.</summary>
    public static IEnumerable<DataSourceType> AllTypes =>
        Enum.GetValues<DataSourceType>();

    /// <summary>All serial behaviours for ComboBox binding (Continuous / ResetPerBatch).</summary>
    public static IEnumerable<SerialMode> AllSerialModes =>
        Enum.GetValues<SerialMode>();
}
