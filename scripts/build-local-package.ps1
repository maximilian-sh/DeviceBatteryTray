param(
    [switch]$Clean,
    [switch]$LaunchInstaller,
    [string]$DestinationPath
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')

Push-Location $repoRoot
try {
    Write-Host "=== DeviceBatteryTray local package builder ===" -ForegroundColor Cyan

    $releaseScript = Join-Path $scriptDir 'release.ps1'
    $releaseParams = @{ Configuration = 'Release' }
    if ($Clean) { $releaseParams.Clean = $true }

    Write-Host "Publishing application (Release configuration)..." -ForegroundColor Cyan
    & $releaseScript @releaseParams

    $publishDir = Join-Path $repoRoot 'LGSTrayUI\bin\Release\net8.0-windows\win-x64\standalone'
    if (-not (Test-Path $publishDir)) {
        throw "Publish output not found: $publishDir"
    }

    [xml]$csprojXml = Get-Content 'LGSTrayUI\LGSTrayUI.csproj'
    $versionPrefix = ($csprojXml.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ } | Select-Object -First 1)
    if (-not $versionPrefix) { $versionPrefix = '0.0.0-local' }

    $artifactsDir = Join-Path $repoRoot 'artifacts\local-package'
    if (-not (Test-Path $artifactsDir)) {
        New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    }

    $zipName = "DeviceBatteryTray-v$versionPrefix-win-x64.zip"
    $zipPath = Join-Path $artifactsDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Write-Host "Creating zip: $zipName" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Local package ready:" -ForegroundColor Green
    Write-Host "  $zipPath" -ForegroundColor Green

    # Always place a copy in Downloads (or specified destination) to mirror first-time setup
    if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
        $DestinationPath = Join-Path $env:USERPROFILE 'Downloads'
    }

    if (-not (Test-Path $DestinationPath)) {
        Write-Host "Creating destination folder: $DestinationPath" -ForegroundColor Cyan
        New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    }

    $downloadZipPath = Join-Path $DestinationPath $zipName
    if (Test-Path $downloadZipPath) {
        Write-Host "Removing existing zip: $downloadZipPath" -ForegroundColor Yellow
        Remove-Item $downloadZipPath -Force
    }

    Write-Host "Copying zip to destination..." -ForegroundColor Cyan
    Copy-Item $zipPath $downloadZipPath -Force
    Write-Host "  -> $downloadZipPath" -ForegroundColor Green

    $folderName = "DeviceBatteryTray-v$versionPrefix-win-x64"
    $extractDir = Join-Path $DestinationPath $folderName
    if (Test-Path $extractDir) {
        Write-Host "Removing existing extracted folder: $extractDir" -ForegroundColor Yellow
        Remove-Item $extractDir -Recurse -Force
    }

    Write-Host "Extracting archive to recreate first-time folder..." -ForegroundColor Cyan
    Expand-Archive -Path $downloadZipPath -DestinationPath $extractDir -Force
    Write-Host "  -> $extractDir" -ForegroundColor Green

    Write-Host "Opening extracted folder in File Explorer..." -ForegroundColor Cyan
    Start-Process explorer.exe $extractDir | Out-Null

    if ($LaunchInstaller) {
        Write-Host "Launching installer (install.ps1) in a new PowerShell window." -ForegroundColor Yellow
        Start-Process -FilePath 'powershell.exe' -WorkingDirectory $extractDir -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','install.ps1'
    }
}
finally {
    Pop-Location
}


