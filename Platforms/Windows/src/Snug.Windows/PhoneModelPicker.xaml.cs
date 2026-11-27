using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Snug.Windows.Core;

namespace Snug.Windows;

/// <summary>
/// One original-catalog phone picker for UU and desktop.
/// Flat, virtualized brand/model rows avoid constructing 800 WPF cards at startup.
/// </summary>
public partial class PhoneModelPicker : System.Windows.Controls.UserControl, INotifyPropertyChanged
{
    private PhoneCatalog? _catalog;
    private PhoneChoice _current = PhoneChoice.Standard;
    private IReadOnlyList<PhoneModelListRow> _filteredEntries = [];

    public IReadOnlyList<PhoneModelListRow> FilteredEntries
    {
        get => _filteredEntries;
        private set
        {
            _filteredEntries = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredEntries)));
        }
    }

    public PhoneChoice? SelectedChoice { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PhoneChoice>? ChoiceSelected;
    public event EventHandler? CloseRequested;

    public PhoneModelPicker()
    {
        InitializeComponent();
        Snug.Windows.Services.PhoneDragScroll.AttachList(ModelsList);
        DataContext = this;
    }

    public void Configure(PhoneCatalog catalog, PhoneChoice current)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        SelectedChoice = null;
        CurrentTitle.Text = current.Title;
        CurrentDimensions.Text = current.Screen.SizeText;
        CustomWidth.Text = current.CustomScreen?.Width.ToString() ?? string.Empty;
        CustomHeight.Text = current.CustomScreen?.Height.ToString() ?? string.Empty;
        CustomError.Visibility = Visibility.Collapsed;
        SearchBox.Clear();
        RefreshBrands();
        UpdateCustomState();
    }

    public void FocusSearch() => SearchBox.Focus();

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => RefreshBrands();

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void RefreshBrands()
    {
        if (_catalog is null) return;
        var query = SearchBox.Text.Trim();
        var brands = _catalog.Search(query);
        var rows = new List<PhoneModelListRow>();

        if (query.Length == 0)
        {
            rows.Add(new PhoneModelListRow(true, PhoneCatalog.GenericBrand, null, false));
            foreach (var model in PhoneCatalog.GenericPresets)
                rows.Add(new PhoneModelListRow(false, PhoneCatalog.GenericBrand, model,
                    (string.Equals(_current.Model, model.Id, StringComparison.OrdinalIgnoreCase) || (_current.Model is null && _current.Screen == model.Screen))));
        }
        foreach (var brand in brands)
        {
            rows.Add(new PhoneModelListRow(true, brand.Name, null, false));
            foreach (var model in brand.Models)
                rows.Add(new PhoneModelListRow(false, brand.Name, model,
                    string.Equals(model.Id, _current.Model, StringComparison.OrdinalIgnoreCase)));
        }

        FilteredEntries = rows;
        NoResults.Visibility = brands.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CurrentSection.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CustomSection.Visibility = query.Length == 0 || brands.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: PhoneModel model }) return;
        Choose(new PhoneChoice(model.Id, model.Screen));
    }

    private void Custom_TextChanged(object sender, TextChangedEventArgs e) => UpdateCustomState();

    private void UpdateCustomState()
    {
        if (UseCustomButton is null) return;
        var valid = int.TryParse(CustomWidth?.Text, out var width)
                    && int.TryParse(CustomHeight?.Text, out var height)
                    && PhoneScreen.Custom(width, height) is not null;
        UseCustomButton.IsEnabled = valid;
        if (valid) CustomError.Visibility = Visibility.Collapsed;
    }

    private void UseCustom_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CustomWidth.Text, out var width)
            || !int.TryParse(CustomHeight.Text, out var height)
            || PhoneScreen.Custom(width, height) is not { } screen)
        {
            CustomError.Text = "请输入有效的屏幕宽度和高度。";
            CustomError.Visibility = Visibility.Visible;
            return;
        }
        Choose(new PhoneChoice(null, screen));
    }

    private void Choose(PhoneChoice choice)
    {
        SelectedChoice = choice;
        ChoiceSelected?.Invoke(this, choice);
    }

    private void Done_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    public sealed record PhoneModelListRow(bool IsHeader, string BrandName,
        PhoneModel? Model, bool IsSelected)
    {
        public string DisplayName => Model?.Name ?? string.Empty;
        public string DisplaySize => Model?.Screen.SizeText ?? string.Empty;
        public string Checkmark => IsSelected ? "✓" : string.Empty;
    }
}
