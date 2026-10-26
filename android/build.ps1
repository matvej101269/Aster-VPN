param([string[]]$Task=@('assembleDebug'))
$ErrorActionPreference='Stop'
# JDK 17 on Windows reads worker argument files in the system code page.
# An ASCII junction prevents corrupted Cyrillic paths in Gradle child processes.
$buildRoot=$PSScriptRoot
if($buildRoot -match '[^\x00-\x7F]') {
 $digest=[System.Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($PSScriptRoot))
 $suffix=([BitConverter]::ToString($digest)).Replace('-','').Substring(0,12)
 $buildRoot=Join-Path $env:TEMP "aster-android-$suffix"
 if(Test-Path -LiteralPath $buildRoot) {
  $link=Get-Item -LiteralPath $buildRoot
  if($link.LinkType -ne 'Junction' -or $link.Target -ne $PSScriptRoot){throw "Build junction points to another directory: $buildRoot"}
 } else {New-Item -ItemType Junction -Path $buildRoot -Target $PSScriptRoot | Out-Null}
}
$env:JAVA_HOME=(Get-ChildItem "$buildRoot/tools/jdk" -Directory|Select-Object -First 1).FullName
$env:ANDROID_HOME="$buildRoot/tools/sdk"
$env:GRADLE_USER_HOME="$buildRoot/tools/gradle-cache"
$sdkPath=$env:ANDROID_HOME.Replace('\','/')
Set-Content -LiteralPath "$buildRoot/local.properties" -Value "sdk.dir=$sdkPath" -Encoding ascii
Push-Location $buildRoot
try { & "$buildRoot/tools/gradle-8.13/bin/gradle.bat" -p $buildRoot --no-daemon $Task } finally {Pop-Location}
if($LASTEXITCODE -ne 0){throw "Android build failed: $Task"}
