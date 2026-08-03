namespace OptiClaw.Core.Models;

public sealed record FrameGenerationSettings(
    bool Enabled,
    string Input,
    string Output,
    int InterpolationCount);
