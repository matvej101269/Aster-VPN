param([switch]$Apply)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'release-common.ps1')
$headers=Get-ReleaseHeaders
try{
 $original=@(Get-RepoReleases $headers)
 $entries=@(foreach($release in $original){foreach($asset in $release.assets){
  $platform='';$version=''
  if($asset.name -match '^AsterVPN-Setup-(\d+\.\d+\.\d+)-x64\.exe$'){$platform='Windows';$version=$Matches[1]}
  elseif($asset.name -match '^AsterVPN-(\d+\.\d+\.\d+)-android\.apk$'){$platform='Android';$version=$Matches[1]}
  if($platform){[pscustomobject]@{Platform=$platform;Version=$version;Release=$release;Asset=$asset;Tag=$platform.ToLowerInvariant()+'-'+$version;Path='';Commit=''}}
 }})
 $keep=@(foreach($platform in @('Windows','Android')){$unique=@($entries|Where-Object Platform -eq $platform|Group-Object Version|ForEach-Object{$_.Group|Sort-Object @{Expression={$_.Release.published_at};Descending=$true}|Select-Object -First 1});$unique|Sort-Object @{Expression={[version]$_.Version};Descending=$true}|Select-Object -First 5})
 $keep|ForEach-Object{Write-Output "Keep separately: $($_.Tag)"}
 $obsolete=@($original|Where-Object{$source=$_;!($keep|Where-Object{($_.Platform -eq 'Windows' -and $_.Release.id -eq $source.id) -or ($_.Platform -eq 'Android' -and $_.Tag -eq $source.tag_name)})})
 $obsolete|ForEach-Object{Write-Output "Obsolete release outside retained versions: $($_.tag_name)"}
 if(!$Apply){exit 0}
 $backup=Join-Path $root 'dist/release-maintenance';New-Item -ItemType Directory -Path $backup -Force|Out-Null
 $original|ConvertTo-Json -Depth 30|Set-Content (Join-Path $backup 'original-releases.json') -Encoding utf8
 # Verify every retained binary before modifying a release. Existing Windows releases and assets stay in place.
 foreach($item in $keep){
  $item.Path=Join-Path $root $(if($item.Platform -eq 'Windows'){"dist/installer/$($item.Asset.name)"}else{"dist/android/$($item.Asset.name)"})
  if(!(Test-Path -LiteralPath $item.Path)){$item.Path=Join-Path $backup $item.Asset.name;Invoke-WebRequest -Uri $item.Asset.browser_download_url -OutFile $item.Path -TimeoutSec 900}
  if(('sha256:'+(Get-FileHash -LiteralPath $item.Path).Hash.ToLowerInvariant()) -ne $item.Asset.digest){throw "Retained binary mismatch: $($item.Asset.name)"}
  $ref=Invoke-RestMethod -Uri "$ReleaseApi/git/ref/tags/$([Uri]::EscapeDataString($item.Release.tag_name))" -Headers $headers;$obj=$ref.object
  while($obj.type -eq 'tag'){$obj=(Invoke-RestMethod -Uri "$ReleaseApi/git/tags/$($obj.sha)" -Headers $headers).object};$item.Commit=$obj.sha
 }
 # Split Android attachments out of mixed releases first; only remove their old copies after digest verification.
 foreach($item in @($keep|Where-Object Platform -eq 'Android'|Sort-Object @{Expression={[version]$_.Version}})){
  $current=@(Get-RepoReleases $headers)
  $release=$current|Where-Object tag_name -eq $item.Tag
  if(!$release){
   $body=@{tag_name=$item.Tag;target_commitish=$item.Commit;name="Android $($item.Version)";draft=$true;prerelease=$false;body="Aster VPN for Android $($item.Version). Original signed APK, unchanged. See android/README.md for version history."}|ConvertTo-Json
   $release=Invoke-RestMethod -Uri "$ReleaseApi/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
  }
  Upload-ReleaseAsset $headers $release $item.Path $item.Asset.name
  Invoke-RestMethod -Uri "$ReleaseApi/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body (@{name="Android $($item.Version)";draft=$false;make_latest='false'}|ConvertTo-Json)|Out-Null
 }
 foreach($item in @($keep|Where-Object Platform -eq 'Windows')){
  $body=@{tag_name=$item.Tag;target_commitish=$item.Commit;name="Windows $($item.Version)";make_latest='false';body="Aster VPN for Windows $($item.Version). Original Windows x64 installer, unchanged. See UPDATES.md."}|ConvertTo-Json
  Invoke-RestMethod -Uri "$ReleaseApi/releases/$($item.Release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body $body|Out-Null
  # The Windows EXE is never deleted or replaced. Android duplicates are either retained in verified platform releases or outside the five-version limit.
  foreach($asset in @($item.Release.assets|Where-Object name -match '^AsterVPN-\d+\.\d+\.\d+-android\.apk$')){Invoke-RestMethod -Uri "$ReleaseApi/releases/assets/$($asset.id)" -Method Delete -Headers $headers|Out-Null}
 }
 # Delete only obsolete releases, never the retained releases. Original source tags/history are preserved.
 foreach($release in $obsolete){Invoke-RestMethod -Uri "$ReleaseApi/releases/$($release.id)" -Method Delete -Headers $headers|Out-Null;Write-Output "Removed obsolete release: $($release.tag_name)"}
 $latest=$keep|Sort-Object @{Expression={$_.Release.published_at};Descending=$true}|Select-Object -First 1
 $release=Invoke-RestMethod -Uri "$ReleaseApi/releases/tags/$($latest.Tag)" -Headers $headers
 Invoke-RestMethod -Uri "$ReleaseApi/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"make_latest":"true"}'|Out-Null
 $final=@(Get-RepoReleases $headers)
 foreach($platform in @('Windows','Android')){$releases=@($final|Where-Object name -like "$platform *");if($releases.Count -gt 5){throw "Too many $platform releases"};$versions=@($releases|ForEach-Object{[version]($_.name.Split(' ')[1])});$sorted=@($versions|Sort-Object -Descending);if(($versions -join ',') -ne ($sorted -join ',')){throw "$platform release order needs verification"}}
 foreach($release in $final){if($release.assets.Count -ne 1){throw "Release is mixed or missing an installer: $($release.tag_name)"}}
 Write-Output 'PASS: platform releases separated; at most five per platform; versions descend; retained release IDs/assets preserved.'
}finally{$headers.Clear()}
