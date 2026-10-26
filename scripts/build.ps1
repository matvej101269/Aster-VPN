param([switch]$SelfContained, [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/AsterVPN.csproj'
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist/AsterVPN' }
$core = Join-Path $root 'vendor/sing-box-1.14.3-windows-amd64'
$xrayVersion = (Get-Content (Join-Path $PSScriptRoot 'xray-version.txt') -Raw).Trim()
$xrayDir = Join-Path $root "vendor/xray-$xrayVersion"
$xrayExe = Join-Path $xrayDir 'xray.exe'
if (!(Test-Path -LiteralPath $xrayExe)) { throw 'Сначала запустите scripts/setup-xray.ps1' }
$versionOutput = & $xrayExe version
if ($LASTEXITCODE -ne 0 -or $versionOutput[0] -notmatch ('^Xray ' + [regex]::Escape($xrayVersion) + ' ')) { throw 'Неверная версия исходного Xray' }
if (!(Test-Path (Join-Path $core 'sing-box.exe'))) { throw 'Сначала запустите scripts/setup-core.ps1' }
if ($SelfContained) {
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
} else {
    dotnet publish $project -c Release --self-contained false -o $output
}
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }
New-Item -ItemType Directory -Force (Join-Path $output 'core') | Out-Null
Copy-Item (Join-Path $core '*') (Join-Path $output 'core') -Force
Copy-Item (Join-Path $root 'vendor/aster-awg.exe') (Join-Path $output 'core') -Force
Copy-Item -LiteralPath $xrayExe -Destination (Join-Path $output 'core/xray.exe') -Force
Copy-Item (Join-Path $xrayDir 'LICENSE') (Join-Path $output 'core/XRAY-LICENSE') -Force
$packedXray = Join-Path $output 'core/xray.exe'
$packedVersion = & $packedXray version
if ($LASTEXITCODE -ne 0 -or $packedVersion[0] -notmatch ('^Xray ' + [regex]::Escape($xrayVersion) + ' ') -or (Get-FileHash $packedXray).Hash -ne (Get-FileHash $xrayExe).Hash) { throw 'Проверка Xray в готовой сборке не прошла' }
Write-Host $packedVersion[0]
Copy-Item (Join-Path $root 'README.md') $output -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $output -Force
if (Test-Path (Join-Path $root 'vendor/licenses')) { Copy-Item (Join-Path $root 'vendor/licenses') $output -Recurse -Force }
Write-Host "Готово: $output\AsterVPN.exe"
