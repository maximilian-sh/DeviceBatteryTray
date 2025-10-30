param(
    [switch]$Clean,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

Push-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)
try {
    Set-Location ..

    $publishDir = Join-Path (Get-Location) ("bin\{0}\net8.0-windows\win-x64\standalone" -f $Configuration)
    if ($Clean -and (Test-Path $publishDir)) {
        Remove-Item $publishDir -Recurse -Force
    }

    dotnet publish .\LGSTrayUI\LGSTrayUI.csproj -c $Configuration -p:PublishProfile=Standalone --nologo

    Write-Host "Published to: $publishDir" -ForegroundColor Green
}
finally {
    Pop-Location
}


