using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using StaffManagement.Models;
using StaffManagement.ViewModels;

namespace StaffManagement.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Several copies may be kept on one PC: show which version this is.
        Title += $" – v{typeof(App).Assembly.GetName().Version?.ToString(3)}";
        Loaded += (_, _) =>
        {
            BuildColumns();
            Vm.ColumnsRebuilt += BuildColumns;
            QuickSearchBox.Focus();
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private static readonly IValueConverter FieldValue = new FieldValueConverter();

    private sealed class FieldValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is StaffRecord r && parameter is FieldDefinition f ? f.Get(r) : null;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Grid columns are generated from the field catalog; visibility follows the "Cột hiển thị" toggles.</summary>
    private void BuildColumns()
    {
        var visibleIf = (IValueConverter)FindResource("VisibleIf");
        var checkMark = (IValueConverter)FindResource("CheckMark");
        var cellStyle = (Style)FindResource("CellText");

        StaffGrid.Columns.Clear();
        foreach (var option in Vm.Columns)
        {
            var f = option.Field;
            bool extra = f.Key.StartsWith(FieldCatalog.ExtraKeyPrefix);
            // Extra Excel columns are not properties of StaffRecord: bind the row and read the value through the field.
            var binding = extra
                ? new Binding { Mode = BindingMode.OneWay, Converter = FieldValue, ConverterParameter = f }
                : new Binding(f.Key) { Mode = BindingMode.OneWay };
            if (f.Kind == FieldKind.Date) binding.StringFormat = "dd/MM/yyyy";
            if (f.Kind == FieldKind.Bool) binding.Converter = checkMark;

            var elementStyle = new Style(typeof(TextBlock), cellStyle);
            if (f.Kind is FieldKind.Number or FieldKind.Bool or FieldKind.Date)
                elementStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));

            var column = new DataGridTextColumn
            {
                Header = f.Header,
                Binding = binding,
                SortMemberPath = f.Key,
                // ponytail: no sorting on extra columns (the view sorts by property name); filter on them instead.
                CanUserSort = !extra,
                // Wide enough for the header text (≈7.5 px per character + sort arrow / padding)
                Width = new DataGridLength(Math.Max(f.Width, f.Header.Length * 7.5 + 22)),
                ElementStyle = elementStyle,
            };
            BindingOperations.SetBinding(column, DataGridColumn.VisibilityProperty,
                new Binding(nameof(ColumnOption.IsVisible)) { Source = option, Converter = visibleIf });
            StaffGrid.Columns.Add(column);
        }
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SelectedRecords = StaffGrid.SelectedItems.OfType<StaffRecord>().ToList();

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only react to double-clicks on a row (not on headers / scrollbars).
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is DataGridRow { Item: StaffRecord record })
            {
                Vm.EditRecordCommand.Execute(record);
                return;
            }
            if (d is DataGridColumnHeader) return;
        }
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Vm.EditRecordCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                Vm.DeleteRecordsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnColumnsPopupClosed(object? sender, EventArgs e) => ColumnsToggle.IsChecked = false;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await Vm.HandleDroppedFilesAsync(files);
    }
}
