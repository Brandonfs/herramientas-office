using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HerramientasOffice.Services;
using HerramientasOffice.Views;

namespace HerramientasOffice;

public sealed class NavEntry
{
    public required string Title { get; init; }
    public required Func<UserControl> Create { get; init; }
    public UserControl? Instance { get; set; }
}

public partial class MainWindow : Window
{
    // Para agregar una funcion nueva (por ejemplo Licencias) basta con anadir una entrada aqui.
    private readonly List<NavEntry> _pages = new()
    {
        new NavEntry { Title = "Actualizaciones de Office", Create = () => new UpdatesPage() },
    };

    private ReleaseInfo? _release;
    private bool _checking;
    private bool _applying;

    public MainWindow()
    {
        InitializeComponent();

        VersionText.Text = $"v{AppInfo.CurrentVersion}";

        if (!AppInfo.IsAdmin)
        {
            AdminBadge.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x3F, 0x1D));
            AdminBadgeText.Text = "Sin administrador";
            AdminBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xD2, 0x8A));
        }

        NavList.ItemsSource = _pages;
        NavList.SelectedIndex = 0;

        Loaded += async (_, _) => await CheckForAppUpdateAsync();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavEntry entry) return;
        entry.Instance ??= entry.Create();
        PageHost.Content = entry.Instance;
    }

    // ---------- Actualizacion de la propia app ----------

    private async void BtnCheckApp_Click(object sender, RoutedEventArgs e) => await CheckForAppUpdateAsync();

    private async Task CheckForAppUpdateAsync()
    {
        if (_checking || _applying) return;
        _checking = true;
        BtnCheckApp.IsEnabled = false;
        UpdateStatus.Text = "Buscando...";

        try
        {
            _release = await AppUpdater.CheckAsync();
            if (_release is null)
            {
                UpdateBanner.Visibility = Visibility.Collapsed;
                UpdateStatus.Text = "Al día";
            }
            else
            {
                BannerText.Text = $"Nueva versión {_release.Tag}";
                BtnApplyUpdate.IsEnabled = true;
                UpdateBanner.Visibility = Visibility.Visible;
                UpdateStatus.Text = $"Disponible {_release.Tag}";
            }
        }
        catch
        {
            UpdateStatus.Text = "Sin conexión";
        }
        finally
        {
            _checking = false;
            BtnCheckApp.IsEnabled = true;
        }
    }

    private void BtnNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_release is null) return;
        Process.Start(new ProcessStartInfo(_release.PageUrl) { UseShellExecute = true });
    }

    private async void BtnApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_release is null || _applying) return;

        var answer = MessageBox.Show(
            $"Se instalará {_release.Tag} y la app se reiniciará. ¿Continuar?",
            "Actualizar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _applying = true;
        BtnApplyUpdate.IsEnabled = false;
        BtnCheckApp.IsEnabled = false;
        BannerProgress.Value = 0;
        BannerProgress.Visibility = Visibility.Visible;
        BannerText.Text = $"Descargando {_release.Tag}...";

        try
        {
            var progress = new Progress<double>(p => BannerProgress.Value = p);
            await AppUpdater.DownloadAndStageAsync(_release, progress);
            BannerText.Text = "Reiniciando...";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            BannerProgress.Visibility = Visibility.Collapsed;
            BannerText.Text = $"Error: {ex.Message}";
            BtnApplyUpdate.IsEnabled = true;
            BtnCheckApp.IsEnabled = true;
            _applying = false;
        }
    }
}
