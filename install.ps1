$ErrorActionPreference = 'Stop'

$Repo       = "akostt/vpn-check"
$InstallDir = "$env:LOCALAPPDATA\VPNCheck"

$Asset = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
    "VPNCheck-win-arm64.zip"
} else {
    "VPNCheck-win-x64.zip"
}

$Url     = "https://github.com/$Repo/releases/latest/download/$Asset"
$Tmp     = Join-Path $env:TEMP "vpncheck_install_$([System.IO.Path]::GetRandomFileName())"
$ZipPath = Join-Path $Tmp $Asset
New-Item -ItemType Directory -Force -Path $Tmp | Out-Null

Write-Host "Downloading $Asset..."
Invoke-WebRequest -Uri $Url -OutFile $ZipPath -UseBasicParsing

Write-Host "Installing..."
Expand-Archive -Path $ZipPath -DestinationPath $Tmp -Force
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Move-Item -Force "$Tmp\VPNCheck.exe" "$InstallDir\VPNCheck.exe"

$CurrentPath = [Environment]::GetEnvironmentVariable("PATH", "User") ?? ""
if ($CurrentPath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$CurrentPath;$InstallDir", "User")
    Write-Host ""
    Write-Host "  Added $InstallDir to PATH (restart terminal to apply)"
}

Remove-Item -Recurse -Force $Tmp

Write-Host ""
Write-Host "v VPNCheck installed -> $InstallDir\VPNCheck.exe"
Write-Host ""
Write-Host "Run: VPNCheck"
