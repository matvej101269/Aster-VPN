param([string]$Tag = 'clients-2026-10-26')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repo = 'matvej101269/Aster-VPN'
$windowsVersion = ([xml](Get-Content (Join-Path $root 'src/AsterVPN.csproj') -Raw)).Project.PropertyGroup.Version
$gradle = Get-Content (Join-Path $root 'android/app/build.gradle') -Raw
$androidVersion = [regex]::Match($gradle, "versionName '([^']+)'").Groups[1].Value
$windows = Join-Path $root "dist/installer/AsterVPN-Setup-$windowsVersion-x64.exe"
$apk = Join-Path $root 'android/app/build/outputs/apk/release/app-release.apk'
if (!(Test-Path $windows) -or !(Test-Path $apk)) { throw 'Build both clients before publishing.' }
# Git Credential Manager supplies credentials; never print or persist the token.
$credentialLines = "protocol=https`nhost=github.com`n`n" | git -c "safe.directory=$($root.Replace('\','/'))" credential fill
if ($LASTEXITCODE -ne 0) { throw 'GitHub authentication unavailable.' }
$tokenLine = $credentialLines | Where-Object { $_.StartsWith('password=') } | Select-Object -First 1
if (!$tokenLine) { throw 'No GitHub credential available.' }
$headers = @{ Authorization = 'Bearer ' + $tokenLine.Substring(9); Accept = 'application/vnd.github+json'; 'User-Agent' = 'AsterVPN-release' }
try {
 $body = @{
  tag_name = $Tag; target_commitish = 'main'; name = "Windows $windowsVersion / Android $androidVersion"
  draft = $true; prerelease = $false
  body = "Auto-update support from this repository. Downloads are checked against GitHub SHA-256 digests. Android also verifies the APK signing certificate. Installation requires user confirmation. Windows: 160 checks passed. Android: 59 tests passed; lint has no errors. Install these versions once to receive future update offers. See UPDATES.md."
 } | ConvertTo-Json
 $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
 foreach ($asset in @(@{Path=$windows; Name=[IO.Path]::GetFileName($windows)}, @{Path=$apk; Name="AsterVPN-$androidVersion-android.apk"})) {
  $url = "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($asset.Name))"
  $uploaded = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $asset.Path -TimeoutSec 900
  $expected = 'sha256:' + (Get-FileHash -LiteralPath $asset.Path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($uploaded.digest -ne $expected) { throw "Uploaded digest mismatch: $($asset.Name). Release remains draft." }
  Write-Output "Verified asset: $($asset.Name)"
 }
 $published = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false}'
 Write-Output $published.html_url
} finally { $headers.Clear(); $credentialLines=$null; $tokenLine=$null }
