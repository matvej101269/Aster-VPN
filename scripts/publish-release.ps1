param([Parameter(Mandatory)][ValidateSet('Android','Windows')][string]$Platform,[Parameter(Mandatory)][string]$ReleaseNotes)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'release-common.ps1')
if($Platform -eq 'Android'){
 $gradle=Get-Content (Join-Path $root 'android/app/build.gradle') -Raw
 $version=[regex]::Match($gradle,"versionName '([^']+)'").Groups[1].Value
 $path=Join-Path $root 'android/app/build/outputs/apk/release/app-release.apk'
 $metadata=Get-Content (Join-Path (Split-Path $path -Parent) 'output-metadata.json') -Raw|ConvertFrom-Json
 if($metadata.elements[0].versionName -ne $version){throw 'Android output is stale; rebuild.'}
 $name="AsterVPN-$version-android.apk"
}else{
 $version=([xml](Get-Content (Join-Path $root 'src/AsterVPN.csproj') -Raw)).Project.PropertyGroup.Version
 $name="AsterVPN-Setup-$version-x64.exe";$path=Join-Path $root "dist/installer/$name"
}
if(!(Test-Path -LiteralPath $path)){throw 'Build the selected platform before publishing.'}
$tag=$Platform.ToLowerInvariant()+'-'+$version
$commit=git -c "safe.directory=$($root.Replace('\','/'))" rev-parse HEAD
if($LASTEXITCODE -ne 0){throw 'Cannot determine source commit.'}
$headers=Get-ReleaseHeaders
try{
 $existing=Invoke-RestMethod -Uri "$ReleaseApi/releases?per_page=100" -Headers $headers
 if($existing|Where-Object tag_name -eq $tag){throw "Release $tag already exists; increase the version."}
 $body=@{tag_name=$tag;target_commitish=$commit;name="$Platform $version";draft=$true;prerelease=$false;body=$ReleaseNotes}|ConvertTo-Json
 $release=Invoke-RestMethod -Uri "$ReleaseApi/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
 Upload-ReleaseAsset $headers $release $path $name
 $published=Invoke-RestMethod -Uri "$ReleaseApi/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"make_latest":"true"}'
 Write-Output $published.html_url
}finally{$headers.Clear()}
