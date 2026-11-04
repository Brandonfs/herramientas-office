using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HerramientasOffice.Models;

public enum UpdateState { Instalada, Disponible, Bloqueada, NoInstalada }

public sealed class UpdateEntry : INotifyPropertyChanged
{
    private UpdateState _state;
    private bool _isProblem;

    public required string Kb { get; init; }
    public required string Title { get; init; }
    public required string Product { get; init; }
    public string? UninstallString { get; init; }
    public string? UpdateId { get; init; }

    public UpdateState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnChanged();
            OnChanged(nameof(StateText));
        }
    }

    public string StateText => State == UpdateState.NoInstalada ? "No instalada" : State.ToString();

    public bool IsProblem
    {
        get => _isProblem;
        set
        {
            if (_isProblem == value) return;
            _isProblem = value;
            OnChanged();
        }
    }

    private DateTime? _publishedDate;
    private string _supersededBy = "";

    /// <summary>Fecha en que Microsoft publico o modifico por ultima vez la actualizacion.</summary>
    public DateTime? PublishedDate
    {
        get => _publishedDate;
        set
        {
            if (_publishedDate == value) return;
            _publishedDate = value;
            OnChanged();
            OnChanged(nameof(PublishedDisplay));
        }
    }

    /// <summary>Fecha de instalacion en este equipo (solo para instaladas).</summary>
    public DateTime? InstalledDate
    {
        get => _installedDate;
        set
        {
            if (_installedDate == value) return;
            _installedDate = value;
            OnChanged();
            OnChanged(nameof(InstalledDisplay));
        }
    }

    private DateTime? _installedDate;

    /// <summary>KBs mas nuevos que declaran reemplazar a esta actualizacion.</summary>
    public string SupersededBy
    {
        get => _supersededBy;
        set
        {
            if (_supersededBy == value) return;
            _supersededBy = value;
            OnChanged();
            OnChanged(nameof(SupersededByDisplay));
        }
    }

    public string PublishedDisplay => PublishedDate?.ToString("dd/MM/yyyy") ?? "—";
    public string InstalledDisplay => InstalledDate?.ToString("dd/MM/yyyy") ?? "—";
    public string SupersededByDisplay => SupersededBy.Length == 0 ? "—" : SupersededBy;

    public string KbDisplay => Kb.Length == 0 ? "—" : Kb;

    public long KbNumber => Kb.Length > 2 && long.TryParse(Kb[2..], out var n) ? n : 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
