using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AsterVPN;

public partial class ProcessPicker : Window
{
    public sealed record ApplicationEntry(string Path, ImageSource? Icon)
    {
        public string Name => System.IO.Path.GetFileName(Path);
    }

    private readonly List<ApplicationEntry> entries;
    public IReadOnlyList<string> SelectedPaths { get; private set; } = [];

    public ProcessPicker(IEnumerable<ApplicationEntry> applications)
    {
        entries = applications.ToList();
        InitializeComponent();
        RefreshList();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public static ApplicationEntry CreateEntry(string path)
    {
        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                image.Freeze();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException) { }
        return new ApplicationEntry(path, image);
    }

    private void SearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ProcessList != null) RefreshList();
    }

    private void RefreshList()
    {
        var visible = entries.Where(item => item.Path.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        ProcessList.ItemsSource = visible;
        CountText.Text = $"{visible.Count} из {entries.Count}";
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AddButton == null) return;
        AddButton.IsEnabled = ProcessList.SelectedItems.Count > 0;
        AddButton.Content = ProcessList.SelectedItems.Count > 0 ? $"Добавить ({ProcessList.SelectedItems.Count})" : "Добавить";
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        SelectedPaths = ProcessList.SelectedItems.Cast<ApplicationEntry>().Select(item => item.Path).ToList();
        DialogResult = true;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
