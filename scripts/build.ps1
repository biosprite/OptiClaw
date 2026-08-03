param(
    [switch]$SkipTests,
    [switch]$SkipZip
)

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "OptiClaw-win-x64"))
$zipPath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "OptiClaw-win-x64.zip"))

if (-not $publishDirectory.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
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

    New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
    dotnet publish ".\src\OptiClaw\OptiClaw.csproj" `
        -c Release `
        -p:Platform=x64 `
        -r win-x64 `
        --self-contained true `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    if (-not $SkipZip) {
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "Created $zipPath"
    }
    else {
        Write-Host "Published $publishDirectory"
    }
}
finally {
    Pop-Location
}

