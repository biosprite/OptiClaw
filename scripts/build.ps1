param(
    [switch]$SkipTests
)

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "single-file-publish"))
$executablePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "OptiClaw.exe"))

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
    dotnet publish ".\src\OptiClaw\OptiClaw.csproj" `
        -c Release `
        -p:Platform=x64 `
        -r win-x64 `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    $publishedExecutable = Join-Path $publishDirectory "OptiClaw.exe"
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

