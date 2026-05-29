using LabelDesigner.Core.Models;
using LabelDesigner.Services;
using System.Collections.ObjectModel;

namespace LabelDesigner.ViewModels;

public class LayerViewModel : ViewModelBase
{
    private readonly Layer _model;
    private bool _isSelected;

    public Guid Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set { if (_model.Name != value) { _model.Name = value; OnPropertyChanged(); } }
    }

    public bool IsVisible
    {
        get => _model.IsVisible;
        set
        {
            if (_model.IsVisible == value) return;
            _model.IsVisible = value;
            OnPropertyChanged();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsHidden
    {
        get => _model.IsHidden;
        set
        {
            if (_model.IsHidden == value) return;
            _model.IsHidden = value;
            OnPropertyChanged();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? PrintCondition
    {
        get => _model.PrintCondition;
        set
        {
            if (_model.PrintCondition != value)
            {
                _model.PrintCondition = value;
                OnPropertyChanged();
                SyncConditionList();
            }
        }
    }

    /// <summary>Individual condition clauses parsed from PrintCondition (split on &amp;&amp;).</summary>
    public ObservableCollection<string> ConditionList { get; } = new();

    private void SyncConditionList()
    {
        ConditionList.Clear();
        foreach (var clause in PrintConditionParser.SplitClauses(_model.PrintCondition))
            ConditionList.Add(clause);
    }

    public void AddCondition(string clause)
    {
        if (string.IsNullOrWhiteSpace(clause)) return;
        ConditionList.Add(clause.Trim());
        _model.PrintCondition = PrintConditionParser.Join(ConditionList);
        OnPropertyChanged(nameof(PrintCondition));
    }

    public void RemoveCondition(string clause)
    {
        ConditionList.Remove(clause);
        _model.PrintCondition = PrintConditionParser.Join(ConditionList);
        OnPropertyChanged(nameof(PrintCondition));
    }

    /// <summary>Available field names for the condition builder — populated by DesignerViewModel.</summary>
    public ObservableCollection<string> AvailableFields { get; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>Raised when IsVisible or IsHidden changes so DesignerViewModel can update element visibility.</summary>
    public event EventHandler? VisibilityChanged;

    public Layer ToModel() => _model;

    public LayerViewModel(Layer model)
    {
        _model = model;
        SyncConditionList();
    }

    public LayerViewModel() : this(new Layer()) { }
}
