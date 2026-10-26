$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vendor = Join-Path $root 'vendor'
New-Item -ItemType Directory -Force $vendor | Out-Null
$release = Invoke-RestMethod 'https://api.github.com/repos/SagerNet/sing-box/releases/tags/v1.14.3'
$asset = $release.assets | Where-Object name -eq 'sing-box-1.14.3-windows-amd64.zip'
if (!$asset -or !$asset.digest) { throw 'GitHub не вернул asset/digest для проверки архива' }
$archive = Join-Path $vendor 'core.zip'
Invoke-WebRequest $asset.browser_download_url -OutFile $archive
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($asset.digest -ne "sha256:$hash") { throw 'Контрольная сумма архива не совпадает' }
Expand-Archive $archive $vendor -Force
"sha256:$hash" | Set-Content (Join-Path $vendor 'SHA256.txt')
