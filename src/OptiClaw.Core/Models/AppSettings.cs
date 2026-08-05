namespace OptiClaw.Core.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "Default";
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}
