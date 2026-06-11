using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

/// <summary>One editable routing rule row.</summary>
public class TemplateRouteRowViewModel : ViewModelBase
{
    private string _field = "";
    private RouteOperator _operator = RouteOperator.Equals;
    private string _value = "";
    private double _min;
    private double _max;
    private string _templateName = "";

    public string Field { get => _field; set => Set(ref _field, value); }

    public RouteOperator Operator
    {
        get => _operator;
        set { if (Set(ref _operator, value)) { OnPropertyChanged(nameof(IsRange)); OnPropertyChanged(nameof(IsValue)); } }
    }

    public string Value { get => _value; set => Set(ref _value, value); }
    public double Min { get => _min; set => Set(ref _min, value); }
    public double Max { get => _max; set => Set(ref _max, value); }
    public string TemplateName { get => _templateName; set => Set(ref _templateName, value); }

    public bool IsRange => Operator == RouteOperator.NumericRange;
    public bool IsValue => !IsRange;

    public TemplateRouteRowViewModel() { }

    public TemplateRouteRowViewModel(TemplateRoute r)
    {
        _field = r.Field; _operator = r.Operator; _value = r.Value;
        _min = r.Min; _max = r.Max; _templateName = r.TemplateName;
    }

    public TemplateRoute ToRoute() => new()
    {
        Field = Field.Trim(), Operator = Operator, Value = Value.Trim(),
        Min = Min, Max = Max, TemplateName = TemplateName.Trim()
    };
}

/// <summary>
/// Backs the Template Routing dialog: the ordered rule list that decides which template a batch
/// row prints with when it has no explicit Template column. First matching rule wins; rules are
/// shared with every station via TemplateRoutes.json in the templates folder.
/// </summary>
public class TemplateRoutingViewModel : ViewModelBase
{
    private TemplateRouteRowViewModel? _selected;
    private string _status = "";

    public ObservableCollection<TemplateRouteRowViewModel> Routes { get; } = new();
    public TemplateRouteRowViewModel? Selected { get => _selected; set => Set(ref _selected, value); }
    public string Status { get => _status; set => Set(ref _status, value); }

    public List<string> TemplateNames { get; } = new();
    public Array Operators { get; } = Enum.GetValues(typeof(RouteOperator));

    public event EventHandler<bool>? CloseRequested;

    public ICommand AddCommand      => new RelayCommand(Add);
    public ICommand RemoveCommand   => new RelayCommand(Remove, () => Selected != null);
    public ICommand MoveUpCommand   => new RelayCommand(MoveUp, () => Selected != null && Routes.IndexOf(Selected) > 0);
    public ICommand MoveDownCommand => new RelayCommand(MoveDown, () => Selected != null && Routes.IndexOf(Selected) < Routes.Count - 1);
    public ICommand SaveCommand     => new RelayCommand(Save);
    public ICommand CancelCommand   => new RelayCommand(() => CloseRequested?.Invoke(this, false));

    public TemplateRoutingViewModel()
    {
        foreach (var r in TemplateRouteStore.Load()) Routes.Add(new TemplateRouteRowViewModel(r));

        try
        {
            var templates = new TemplateService(AppConfig.TemplatesDir);
            foreach (var path in templates.GetTemplatePaths())
            {
                var t = templates.Load(path);
                if (t != null) TemplateNames.Add(t.Name);
            }
            TemplateNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { LogService.Warn($"Could not list templates for routing dialog: {ex.Message}"); }
    }

    private void Add()
    {
        var row = new TemplateRouteRowViewModel();
        Routes.Add(row);
        Selected = row;
    }

    private void Remove()
    {
        if (Selected != null) Routes.Remove(Selected);
    }

    private void MoveUp()
    {
        if (Selected == null) return;
        int i = Routes.IndexOf(Selected);
        if (i > 0) Routes.Move(i, i - 1);
    }

    private void MoveDown()
    {
        if (Selected == null) return;
        int i = Routes.IndexOf(Selected);
        if (i >= 0 && i < Routes.Count - 1) Routes.Move(i, i + 1);
    }

    private void Save()
    {
        var bad = Routes.FirstOrDefault(r =>
            string.IsNullOrWhiteSpace(r.Field) || string.IsNullOrWhiteSpace(r.TemplateName));
        if (bad != null)
        {
            Status = "Every rule needs a Field and a Template.";
            Selected = bad;
            return;
        }

        try
        {
            TemplateRouteStore.Save(Routes.Select(r => r.ToRoute()).ToList());
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            LogService.Error("Saving template routes failed.", ex);
            Status = "Could not save: " + ex.Message;
        }
    }
}
