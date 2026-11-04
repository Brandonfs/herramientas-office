using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HerramientasOffice.Models;
using HerramientasOffice.Services;

namespace HerramientasOffice.Views;

public partial class UpdatesPage : UserControl
{
    private readonly ObservableCollection<UpdateEntry> _items = new();
    private readonly HashSet<string> _problemKbs = ProblemKbStore.Load();
    private readonly bool _isAdmin = AppInfo.IsAdmin;
    private ICollectionView? _view;
    private bool _busy;
    private bool _searched;

    public UpdatesPage()
    {
        InitializeComponent();

        if (!_isAdmin)
            AdminWarning.Visibility = Visibility.Visible;

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        _view.SortDescriptions.Add(new SortDescription(nameof(UpdateEntry.KbNumber), ListSortDirection.Descending));
        UpdatesGrid.ItemsSource = _view;

        // La busqueda no arranca sola: se hace con el boton "Actualizar".
        UpdateSummary();
    }

    // ---------- Filtros ----------

    private bool FilterItem(object obj)
    {
        var e = (UpdateEntry)obj;

        // Los KB marcados como problematicos se muestran siempre, sin importar el producto.
        bool productOk = e.IsProblem || ProductCombo.SelectedIndex switch
        {
            0 => e.Product == "Excel",
            1 => e.Product == "Word",
            _ => true,
        };
        if (!productOk) return false;

        switch (StateCombo.SelectedIndex)
        {
            case 1 when e.State != UpdateState.Instalada: return false;
            case 2 when e.State != UpdateState.Disponible: return false;
            case 3 when e.State != UpdateState.Bloqueada: return false;
        }

        var text = SearchBox.Text.Trim();
        if (text.Length > 0 &&
            !e.Kb.Contains(text, StringComparison.OrdinalIgnoreCase) &&
            !e.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void Filters_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (_view is null) return;
        _view.Refresh();
        UpdateSummary();
        UpdateButtons();
    }

    private void UpdateSummary()
    {
        if (_view is null) return;
        var visible = _view.Cast<UpdateEntry>().ToList();
        int inst = visible.Count(e => e.State == UpdateState.Instalada);
        int disp = visible.Count(e => e.State == UpdateState.Disponible);
        int bloq = visible.Count(e => e.State == UpdateState.Bloqueada);
        SummaryText.Text = $"{inst} instaladas  ·  {disp} disponibles  ·  {bloq} bloqueadas";
        EmptyText.Text = _searched ? "Sin resultados" : "Pulsa Actualizar para buscar";
        EmptyText.Visibility = visible.Count == 0 && !_busy ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Estado de la UI ----------

    private List<UpdateEntry> Selected() => UpdatesGrid.SelectedItems.Cast<UpdateEntry>().ToList();

    private void UpdatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var sel = Selected();
        BtnRefresh.IsEnabled = !_busy;
        BtnUninstall.IsEnabled = !_busy && _isAdmin && sel.Any(s => s.State == UpdateState.Instalada && !string.IsNullOrEmpty(s.UninstallString));
        BtnBlock.IsEnabled = !_busy && _isAdmin && sel.Any(s => s.State == UpdateState.Disponible && s.UpdateId is not null);
        BtnEnable.IsEnabled = !_busy && _isAdmin && sel.Any(s => s.State == UpdateState.Bloqueada && s.UpdateId is not null);
        BtnMark.IsEnabled = !_busy && sel.Any(s => s.Kb.Length > 0);
        BtnCatalog.IsEnabled = sel.Any(s => s.Kb.Length > 0);
    }

    private void BtnCatalog_Click(object sender, RoutedEventArgs e)
    {
        var kb = Selected().Select(s => s.Kb).FirstOrDefault(k => k.Length > 0);
        if (kb is null) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            $"https://www.catalog.update.microsoft.com/Search.aspx?q={kb}") { UseShellExecute = true });
    }

    private void SetBusy(bool busy, string status = "")
    {
        _busy = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
        UpdateButtons();
        UpdateSummary();
    }

    private void Log(string message)
    {
        LogPanel.Visibility = Visibility.Visible;
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    // ---------- Carga de datos ----------

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _searched = true;
        SetBusy(true, "Buscando...");

        try
        {
            // Todo arranca en paralelo; cada parte se muestra en cuanto esta lista.
            var installedTask = Task.Run(InstalledUpdatesService.GetInstalled);
            var historyTask = Task.Run(WindowsUpdateService.GetInstallDates);
            var pendingTask = Task.Run(WindowsUpdateService.SearchPending);
            var wuaInstalledTask = Task.Run(WindowsUpdateService.SearchInstalled);

            _items.Clear();
            var installedList = await installedTask;
            var installDates = await historyTask;
            foreach (var u in installedList)
            {
                u.InstalledDate ??= installDates.TryGetValue(u.Kb, out var d) ? d : null;
                AddItem(u);
            }
            ApplyFilters();

            try
            {
                var pending = await pendingTask;
                foreach (var w in pending)
                {
                    AddItem(new UpdateEntry
                    {
                        Kb = w.Kb,
                        Title = w.Title,
                        Product = w.Product,
                        State = w.Hidden ? UpdateState.Bloqueada : UpdateState.Disponible,
                        UpdateId = w.UpdateId,
                        PublishedDate = w.Date,
                    });
                }
                ApplyFilters();
                StatusText.Text = "Completando...";

                var wua = pending.Concat(await wuaInstalledTask).ToList();
                EnrichFromWindowsUpdate(wua);
                AddMissingProblemKbs();
            }
            catch (Exception ex)
            {
                Log($"Windows Update no disponible: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            ApplyFilters();
        }
    }

    /// <summary>
    /// Completa la fecha de publicacion y detecta que KBs mas nuevos declaran reemplazar
    /// a cada actualizacion (posibles correcciones publicadas por Microsoft).
    /// </summary>
    private void EnrichFromWindowsUpdate(List<WuaItem> wua)
    {
        foreach (var entry in _items)
        {
            var same = wua.Where(w =>
                (entry.UpdateId is not null && string.Equals(w.UpdateId, entry.UpdateId, StringComparison.OrdinalIgnoreCase)) ||
                (entry.UpdateId is null && entry.Kb.Length > 0 && string.Equals(w.Kb, entry.Kb, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (same.Count == 0) continue;

            entry.PublishedDate ??= same.Max(w => w.Date);

            var ids = same.Select(w => w.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var replacers = wua
                .Where(w => !ids.Contains(w.UpdateId) && w.Supersedes.Any(ids.Contains))
                .Select(w => w.Kb.Length > 0 ? w.Kb : "(sin KB)")
                .Where(k => !string.Equals(k, entry.Kb, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(k => k)
                .ToList();
            entry.SupersededBy = string.Join(", ", replacers);

            if (entry.IsProblem && replacers.Count > 0)
                Log($"{entry.KbDisplay} fue reemplazada por {entry.SupersededBy}.");
        }
    }

    private static readonly Dictionary<string, string> KnownProducts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KB5002914"] = "Excel",
        ["KB5002934"] = "Excel",
    };

    /// <summary>
    /// Los KB problematicos siempre aparecen en la lista; si el equipo no los tiene
    /// instalados ni Windows Update los ofrece, se muestran como "No instalada".
    /// </summary>
    private void AddMissingProblemKbs()
    {
        foreach (var kb in _problemKbs.OrderBy(k => k))
        {
            if (_items.Any(i => string.Equals(i.Kb, kb, StringComparison.OrdinalIgnoreCase))) continue;

            AddItem(new UpdateEntry
            {
                Kb = kb,
                Title = "No instalada ni ofrecida por Windows Update en este equipo",
                Product = KnownProducts.TryGetValue(kb, out var product) ? product : "Office",
                State = UpdateState.NoInstalada,
            });
        }
    }

    private void AddItem(UpdateEntry entry)
    {
        entry.IsProblem = _problemKbs.Contains(entry.Kb);
        _items.Add(entry);
    }

    // ---------- Acciones ----------

    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        var targets = Selected()
            .Where(s => s.State == UpdateState.Instalada && !string.IsNullOrEmpty(s.UninstallString))
            .ToList();
        if (targets.Count == 0) return;

        var names = string.Join(Environment.NewLine, targets.Select(t => "  • " + (t.Kb.Length > 0 ? t.Kb : t.Title)));
        var answer = MessageBox.Show(
            $"Desinstalar:{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}" +
            "Cierra antes Excel y Word. ¿Continuar?",
            "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true, "Desinstalando...");
        foreach (var t in targets)
        {
            try
            {
                int code = await Task.Run(() => UninstallRunner.Run(t.UninstallString!));
                Log($"{t.KbDisplay}: desinstalada (código {code}).");
            }
            catch (Exception ex)
            {
                Log($"{t.KbDisplay}: error: {ex.Message}");
            }
        }
        SetBusy(false);
        await RefreshAsync();
    }

    private async void BtnBlock_Click(object sender, RoutedEventArgs e) =>
        await SetHiddenAsync(UpdateState.Disponible, hidden: true);

    private async void BtnEnable_Click(object sender, RoutedEventArgs e) =>
        await SetHiddenAsync(UpdateState.Bloqueada, hidden: false);

    private async Task SetHiddenAsync(UpdateState fromState, bool hidden)
    {
        var targets = Selected().Where(s => s.State == fromState && s.UpdateId is not null).ToList();
        if (targets.Count == 0) return;

        SetBusy(true, hidden ? "Bloqueando..." : "Habilitando...");

        try
        {
            var ids = targets.Select(t => t.UpdateId!).ToList();
            var changed = await Task.Run(() => WindowsUpdateService.SetHidden(ids, hidden));

            foreach (var t in targets)
            {
                if (changed.Contains(t.UpdateId!))
                {
                    t.State = hidden ? UpdateState.Bloqueada : UpdateState.Disponible;
                    Log($"{t.KbDisplay}: {(hidden ? "bloqueada" : "habilitada")}.");
                }
                else
                {
                    Log($"{t.KbDisplay}: no encontrada en Windows Update. Actualiza la lista.");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            ApplyFilters();
        }
    }

    private void BtnMark_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected().Where(s => s.Kb.Length > 0).ToList();
        if (sel.Count == 0) return;

        bool mark = sel.Any(s => !s.IsProblem);
        foreach (var kb in sel.Select(s => s.Kb).Distinct())
        {
            if (mark) _problemKbs.Add(kb); else _problemKbs.Remove(kb);
        }
        foreach (var item in _items)
            item.IsProblem = _problemKbs.Contains(item.Kb);

        try
        {
            ProblemKbStore.Save(_problemKbs);
            Log(mark ? "Marcados." : "Desmarcados.");
        }
        catch (Exception ex)
        {
            Log($"No se pudo guardar: {ex.Message}");
        }
    }
}
