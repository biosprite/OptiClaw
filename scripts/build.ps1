param(
    [string]$Tag,
    [switch]$SkipTests
)

$releaseName = "OptiClaw"
$packageVersion = "1.1.0.0"
if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $tagValue = $Tag.Trim()
    if ($tagValue -notmatch '^v?(?<version>\d+\.\d+(?:\.\d+){0,2})(?:-[0-9A-Za-z.-]+)?$') {
        throw "Tag must look like v1.1, v1.1.0, or v1.1.0-beta.1."
    }

    $normalizedTag = if ($tagValue.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
        "v$($tagValue.Substring(1))"
    } else {
        "v$tagValue"
    }
    $releaseName = "OptiClaw-$normalizedTag"
    $versionParts = $Matches.version.Split('.')
    while ($versionParts.Count -lt 4) {
        $versionParts += "0"
    }
    $packageVersion = $versionParts -join '.'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$packageBuildDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "msix-build"))
$packagePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "$releaseName.msixupload"))

if (-not $packageBuildDirectory.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $packagePath.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to package outside the repository artifacts directory."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio Installer could not be found. Install Visual Studio with MSIX packaging tools."
}

$msbuildCandidates = @(& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe")
$msbuild = $msbuildCandidates | Where-Object { $_ -match '\\amd64\\MSBuild\.exe$' } | Select-Object -First 1
if (-not $msbuild) {
    $msbuild = $msbuildCandidates | Select-Object -First 1
}
if (-not $msbuild -or -not (Test-Path -LiteralPath $msbuild)) {
    throw "Visual Studio MSBuild could not be found."
}

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        dotnet test ".\tests\OptiClaw.Core.Tests\OptiClaw.Core.Tests.csproj" -c Release
        if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
    }

    if (Test-Path -LiteralPath $packageBuildDirectory) {
        Remove-Item -LiteralPath $packageBuildDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    New-Item -ItemType Directory -Force -Path $packageBuildDirectory | Out-Null

    $packageOutput = "$packageBuildDirectory\"
    & $msbuild ".\src\OptiClaw\OptiClaw.csproj" `
        /restore `
        /t:Rebuild `
        /p:Configuration=Release `
        /p:Platform=x64 `
        /p:RuntimeIdentifier=win-x64 `
        /p:GenerateAppxPackageOnBuild=true `
        /p:AppxPackageSigningEnabled=false `
        /p:AppxBundle=Never `
        /p:UapAppxPackageBuildMode=StoreUpload `
        /p:AppxPackageVersion=$packageVersion `
        /p:AppxPackageDir=$packageOutput `
        /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "MSIX packaging failed." }

    $packages = @(Get-ChildItem -LiteralPath $packageBuildDirectory -Filter *.msixupload -File -Recurse)
    if ($packages.Count -ne 1) {
        throw "Expected one .msixupload package, found $($packages.Count)."
    }

    Move-Item -LiteralPath $packages[0].FullName -Destination $packagePath
    Write-Host "Created $packagePath"
}
finally {
    if (Test-Path -LiteralPath $packageBuildDirectory) {
        Remove-Item -LiteralPath $packageBuildDirectory -Recurse -Force
    }
    Pop-Location
}
