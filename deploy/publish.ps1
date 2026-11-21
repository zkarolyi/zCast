# Builds a self-contained linux-arm64 package for the Raspberry Pi.
# Usage (from the repo root):  powershell -ExecutionPolicy Bypass -File deploy\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pkg  = Join-Path $root 'out\zcast-linux-arm64'
$tgz  = Join-Path $root 'out\zcast-linux-arm64.tar.gz'

if (Test-Path $pkg) { Remove-Item -Recurse -Force $pkg }
if (Test-Path $tgz) { Remove-Item -Force $tgz }

dotnet publish (Join-Path $root 'web\web.csproj') -c Release -r linux-arm64 --self-contained true -o (Join-Path $pkg 'app')
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $PSScriptRoot 'install.sh'), (Join-Path $PSScriptRoot 'zcast.service'), (Join-Path $PSScriptRoot 'nginx-zcast.conf') $pkg

tar -czf $tgz -C (Join-Path $root 'out') 'zcast-linux-arm64'
if ($LASTEXITCODE -ne 0) { throw "tar failed" }
Write-Host "Package: $tgz"
