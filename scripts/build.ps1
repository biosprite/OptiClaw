param(
    [string]$Tag,
    [switch]$SkipTests
)

$releaseName = "OptiClaw"
$releaseVersion = $null
if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $tagValue = $Tag.Trim()
    if ($tagValue -notmatch '^v?\d+\.\d+(?:\.\d+){0,2}(?:-[0-9A-Za-z.-]+)?$') {
        throw "Tag must look like v1.0, v1.0.0, or v1.0.0-beta.1."
    }

    $normalizedTag = if ($tagValue.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
        "v$($tagValue.Substring(1))"
    } else {
        "v$tagValue"
    }
    $releaseName = "OptiClaw-$normalizedTag"
    $releaseVersion = $normalizedTag.Substring(1)
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "single-file-publish"))
$executablePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "$releaseName.exe"))

if (-not $publishDirectory.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $executablePath.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to publish outside the repository artifacts directory."
}

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        dotnet test ".\tests\OptiClaw.Core.Tests\OptiClaw.Core.Tests.csproj" -c Release
        if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
    }

    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $executablePath) {
        Remove-Item -LiteralPath $executablePath -Force
    }

    New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
    $publishArguments = @(
        ".\src\OptiClaw\OptiClaw.csproj",
        "-c", "Release",
        "-p:Platform=x64",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $publishDirectory
    )
    if ($releaseVersion) {
        $publishArguments += "-p:OptiClawReleaseName=$releaseName"
        $publishArguments += "-p:Version=$releaseVersion"
    }

    dotnet publish @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    $publishedExecutable = Join-Path $publishDirectory "$releaseName.exe"
    if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
        throw "Single-file executable was not produced."
    }

    Move-Item -LiteralPath $publishedExecutable -Destination $executablePath
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    Write-Host "Created $executablePath"
}
finally {
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    Pop-Location
}

