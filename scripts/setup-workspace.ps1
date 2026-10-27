param([switch]$AndroidOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'verify-dependencies.ps1')
foreach($tool in (Get-Content (Join-Path $root 'build-tools/tool-downloads.json') -Raw|ConvertFrom-Json)){
 if($AndroidOnly -and $tool.name -eq 'Go'){continue}
 if(Test-Path -LiteralPath (Join-Path $root $tool.probe)){continue}
 $archive=Join-Path $root $tool.archive;$destination=Join-Path $root $tool.destination
 New-Item -ItemType Directory -Force $destination,(Split-Path $archive -Parent)|Out-Null
 if(!(Test-Path $archive)){Invoke-WebRequest -Uri $tool.url -OutFile $archive}
 if((Get-FileHash $archive).Hash.ToLowerInvariant() -ne $tool.sha256){throw "Download hash mismatch: $($tool.name)"}
 Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
}
$env:JAVA_HOME=(Get-ChildItem (Join-Path $root 'android/tools/jdk') -Directory|Select-Object -First 1).FullName
$sdk=Join-Path $root 'android/tools/sdk'
if(!(Test-Path (Join-Path $sdk 'platforms/android-35/android.jar')) -or !(Test-Path (Join-Path $sdk 'build-tools/35.0.0/apksigner.bat'))){
 $sdkmanager=Join-Path $sdk 'cmdline-tools/bin/sdkmanager.bat'
 (1..100|ForEach-Object{'y'})|& $sdkmanager "--sdk_root=$sdk" --licenses
 if($LASTEXITCODE -ne 0){throw 'Android SDK license setup failed.'}
 & $sdkmanager "--sdk_root=$sdk" 'platform-tools' 'platforms;android-35' 'build-tools;35.0.0'
 if($LASTEXITCODE -ne 0){throw 'Android SDK package setup failed.'}
}
if(!$AndroidOnly){
 if(!(Get-Command dotnet -ErrorAction SilentlyContinue)){throw 'Install .NET 8 SDK (winget install Microsoft.DotNet.SDK.8), then rerun.'}
 if(!(Test-Path (Join-Path $root 'vendor/sing-box-1.14.3-windows-amd64/sing-box.exe'))){& (Join-Path $PSScriptRoot 'setup-core.ps1')}
 if(!(Test-Path (Join-Path $root 'vendor/xray-26.10.21/xray.exe'))){& (Join-Path $PSScriptRoot 'setup-xray.ps1')}
 if(!(Test-Path (Join-Path $root 'vendor/aster-awg.exe'))){& (Join-Path $PSScriptRoot 'build-awg.ps1')}
 if(!(Test-Path (Join-Path $root 'vendor/inno-7.1.0/ISCC.exe'))){Expand-Archive -LiteralPath (Join-Path $root 'build-tools/inno-7.1.0.zip') -DestinationPath (Join-Path $root 'vendor/inno-7.1.0') -Force}
}
Write-Output 'Workspace tools are ready. Restore the signing backup separately to build Android updates with the existing certificate.'
