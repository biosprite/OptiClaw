using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace OptiClaw.Core.Models;

public sealed class GameProfile : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _executablePath = string.Empty;
    private string _installDirectory = string.Empty;
    private string _deploymentDirectory = string.Empty;
    private GameSource _source;
    private string[] _detectedTechnologies = [];
    private DateTimeOffset? _lastScannedAt;
    private Guid? _activeInstallId;
    private string? _installedVersion;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set => SetField(ref _executablePath, value);
    }

    public string InstallDirectory
    {
        get => _installDirectory;
        set => SetField(ref _installDirectory, value);
    }

    public string DeploymentDirectory
    {
        get => _deploymentDirectory;
        set => SetField(ref _deploymentDirectory, value);
    }

    public GameSource Source
    {
        get => _source;
        set => SetField(ref _source, value);
    }

    public string[] DetectedTechnologies
    {
        get => _detectedTechnologies;
        set
        {
            if (SetField(ref _detectedTechnologies, value))
            {
                OnPropertyChanged(nameof(TechnologySummary));
            }
        }
    }

    public DateTimeOffset? LastScannedAt
    {
        get => _lastScannedAt;
        set => SetField(ref _lastScannedAt, value);
    }

    public Guid? ActiveInstallId
    {
        get => _activeInstallId;
        set
        {
            if (SetField(ref _activeInstallId, value))
            {
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(StatusSummary));
            }
        }
    }

    public string? InstalledVersion
    {
        get => _installedVersion;
        set
        {
            if (SetField(ref _installedVersion, value))
            {
                OnPropertyChanged(nameof(StatusSummary));
            }
        }
    }

    [JsonIgnore]
    public bool IsInstalled => ActiveInstallId is not null;

    [JsonIgnore]
    public string TechnologySummary => DetectedTechnologies.Length == 0
        ? "No supported upscaler DLL detected"
        : string.Join("  •  ", DetectedTechnologies);

    [JsonIgnore]
    public string StatusSummary => IsInstalled
        ? $"XeSS enabled · OptiScaler {InstalledVersion ?? "installed"}"
        : TechnologySummary;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

