$ErrorActionPreference = 'Stop'

$Repo       = "akostt/vpn-check"
$InstallDir = "$env:LOCALAPPDATA\VPNCheck"

# Detect architecture
$Asset = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
    "VPNCheck-win-arm64.zip"
} else {
    "VPNCheck-win-x64.zip"
}

Write-Host "Fetching latest release..."
$Release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -UseBasicParsing
$Tag = $Release.tag_name
$Url = "https://github.com/$Repo/releases/download/$Tag/$Asset"

$Tmp     = Join-Path $env:TEMP "vpncheck_install_$([System.IO.Path]::GetRandomFileName())"
$ZipPath = Join-Path $Tmp $Asset
New-Item -ItemType Directory -Force -Path $Tmp | Out-Null

Write-Host "Downloading $Asset ($Tag)..."
Invoke-WebRequest -Uri $Url -OutFile $ZipPath -UseBasicParsing

Write-Host "Installing..."
Expand-Archive -Path $ZipPath -DestinationPath $Tmp -Force
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Move-Item -Force "$Tmp\VPNCheck.exe" "$InstallDir\VPNCheck.exe"

# Add to user PATH if missing
$CurrentPath = [Environment]::GetEnvironmentVariable("PATH", "User") ?? ""
if ($CurrentPath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$CurrentPath;$InstallDir", "User")
    Write-Host ""
    Write-Host "  Added $InstallDir to PATH (restart terminal to apply)"
}

Remove-Item -Recurse -Force $Tmp

Write-Host ""
Write-Host "v VPNCheck $Tag installed -> $InstallDir\VPNCheck.exe"
Write-Host ""
Write-Host "Run: VPNCheck"
