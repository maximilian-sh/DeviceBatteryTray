param(
	[switch]$Clean,
	[ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

Push-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)
try {
	Set-Location ..

	$solutionRoot = Get-Location
	$uiWinX64Dir = Join-Path $solutionRoot ("LGSTrayUI\bin\{0}\net8.0-windows\win-x64" -f $Configuration)
	$publishDir = Join-Path $uiWinX64Dir "standalone"

	# Clean previous artifacts and intermediate outputs when requested
	if ($Clean) {
		$pathsToClean = @()
		$pathsToClean += $publishDir
		$pathsToClean += (Join-Path $solutionRoot "LGSTrayUI\obj\$Configuration")
		$pathsToClean += (Join-Path $solutionRoot "LGSTrayHID\bin\$Configuration")
		$pathsToClean += (Join-Path $solutionRoot "LGSTrayHID\obj\$Configuration")
		$pathsToClean += (Join-Path $solutionRoot "LGSTrayCore\bin\$Configuration")
		$pathsToClean += (Join-Path $solutionRoot "LGSTrayCore\obj\$Configuration")
		$pathsToClean += (Join-Path $solutionRoot "LGSTrayPrimitives\bin\$Configuration")
		$pathsToClean += (Join-Path $solutionRoot "LGSTrayPrimitives\obj\$Configuration")
		foreach ($p in $pathsToClean) {
			if (Test-Path $p) { Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue }
		}
	}

	dotnet publish .\LGSTrayUI\LGSTrayUI.csproj -c $Configuration -p:PublishProfile=Standalone --nologo

	# After publish, remove noisy per-project win-x64 output except the 'standalone' folder
	if (Test-Path $uiWinX64Dir) {
		Get-ChildItem $uiWinX64Dir -Force | Where-Object { $_.Name -ne 'standalone' } | ForEach-Object {
			try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch {}
		}
	}

	Write-Host "Published to: $publishDir" -ForegroundColor Green
}
finally {
	Pop-Location
}


