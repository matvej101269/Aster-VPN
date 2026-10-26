$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vendor = Join-Path $root 'vendor'
$xrayVersion = (Get-Content (Join-Path $PSScriptRoot 'xray-version.txt') -Raw).Trim()
$release = Invoke-RestMethod "https://api.github.com/repos/XTLS/Xray-core/releases/tags/v$xrayVersion"
$asset = $release.assets | Where-Object name -eq 'Xray-windows-64.zip'
$archive = Join-Path $vendor "xray-$xrayVersion.zip"
& curl.exe -L --fail --retry 3 --retry-all-errors $asset.browser_download_url -o $archive
if ($LASTEXITCODE -ne 0) { throw 'Xray download failed' }
if (!$asset.digest -or $asset.digest -ne ('sha256:'+(Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant())) { throw 'Xray hash mismatch' }
Expand-Archive $archive (Join-Path $vendor "xray-$xrayVersion") -Force
$release.tag_name | Set-Content (Join-Path $vendor 'XRAY-VERSION.txt')
Write-Output ('Xray downloaded: '+$release.tag_name)
