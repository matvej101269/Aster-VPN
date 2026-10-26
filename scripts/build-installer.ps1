param([string]$PublishedApp = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $root 'vendor/inno-7.1.0/ISCC.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Portable Inno Setup 7.1.0 is required in vendor/inno-7.1.0.' }
$payload = Join-Path $root ('dist/installer-payload/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payload | Out-Null
if (!$PublishedApp) {
    $PublishedApp = Join-Path $payload 'publish'
    dotnet publish (Join-Path $root 'src/AsterVPN.csproj') -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json -p:DebugType=None -p:DebugSymbols=false -o $PublishedApp
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
}
$PublishedApp = (Resolve-Path -LiteralPath $PublishedApp).Path
$runtime = Get-Content (Join-Path $PublishedApp 'AsterVPN.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'Payload must include its own .NET runtime' }
$app = Join-Path $payload 'app'
New-Item -ItemType Directory -Path $app | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $PublishedApp -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($PublishedApp, $file.FullName)
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Links cannot enter installer' }
    if ($file.Extension -notin '.exe','.dll','.json') { throw "Unexpected publish file: $relative" }
    if ($file.Extension -eq '.json' -and $relative -notin 'AsterVPN.deps.json','AsterVPN.runtimeconfig.json') { throw "Unexpected JSON: $relative" }
    $destination = Join-Path $app $relative
    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
$core = Join-Path $app 'core'
New-Item -ItemType Directory -Path $core | Out-Null
foreach ($name in 'sing-box.exe','libcronet.dll','LICENSE') {
    Copy-Item -LiteralPath (Join-Path $root "vendor/sing-box-1.14.3-windows-amd64/$name") -Destination $core
}
Copy-Item -LiteralPath (Join-Path $root 'vendor/aster-awg.exe') -Destination $core
$xrayVersion = (Get-Content (Join-Path $PSScriptRoot 'xray-version.txt') -Raw).Trim()
Copy-Item -LiteralPath (Join-Path $root "vendor/xray-$xrayVersion/xray.exe") -Destination $core
Copy-Item -LiteralPath (Join-Path $root "vendor/xray-$xrayVersion/LICENSE") -Destination (Join-Path $core 'XRAY-LICENSE')
$actualVersion = & (Join-Path $core 'xray.exe') version
if ($LASTEXITCODE -ne 0 -or $actualVersion[0] -notmatch ('^Xray ' + [regex]::Escape($xrayVersion) + ' ')) { throw 'Wrong Xray version in payload' }
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $app
$licenses = Join-Path $root 'vendor/licenses'
if (Test-Path -LiteralPath $licenses) { Copy-Item -LiteralPath $licenses -Destination (Join-Path $app 'licenses') -Recurse }
$manifest = @()
$entries = @()
foreach ($file in Get-ChildItem -LiteralPath $app -File -Recurse | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($app, $file.FullName)
    if ($relative -match '(?i)(settings\.bin|subscription|runtime\.json|cache\.db|(^|[\\/])\.private([\\/]|$)|\.log$|\.pdb$|\.conf$)') { throw "Private/debug file rejected: $relative" }
    $subdirectory = [IO.Path]::GetDirectoryName($relative)
    $dest = if ($subdirectory) { '{app}\' + $subdirectory } else { '{app}' }
    $entries += 'Source: "{#PayloadDir}\' + $relative + '"; DestDir: "' + $dest + '"; Flags: ignoreversion'
    $manifest += [ordered]@{Path=$relative; Bytes=$file.Length; SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash}
}
if (!(Test-Path (Join-Path $app 'coreclr.dll')) -or !(Test-Path (Join-Path $app 'PresentationFramework.dll'))) { throw '.NET/WPF runtime missing' }
$output = Join-Path $root 'dist/installer'
New-Item -ItemType Directory -Force $output | Out-Null
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'payload-manifest.json') -Encoding utf8
$entries | Set-Content -LiteralPath (Join-Path $root 'installer/payload-files.iss') -Encoding utf8
$appVersion = ([xml](Get-Content (Join-Path $root "src/AsterVPN.csproj") -Raw)).Project.PropertyGroup.Version
& $compiler /Q "/DAppVersion=$appVersion" "/DPayloadDir=$app" (Join-Path $root 'installer/AsterVPN.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
Get-ChildItem -LiteralPath $output -Filter '*.exe' | ForEach-Object { Get-FileHash -LiteralPath $_.FullName } | Format-Table
Write-Host "Audited payload: $($manifest.Count) files. No user settings or subscriptions included."
