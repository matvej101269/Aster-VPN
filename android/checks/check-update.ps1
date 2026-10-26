param([string]$Serial = 'emulator-5554')
$ErrorActionPreference = 'Stop'
$androidRoot = Split-Path $PSScriptRoot -Parent
$adb = Join-Path $androidRoot 'tools/sdk/platform-tools/adb.exe'
function AdbCall([string[]]$Arguments) {
 $output = & $adb -s $Serial @Arguments
 if ($LASTEXITCODE -ne 0) { throw "ADB failed: $Arguments" }
 return $output
}
function Read-Ui {
 for ($attempt=0; $attempt -lt 5; $attempt++) {
  $dump = AdbCall @('shell','uiautomator','dump','/sdcard/aster-update-check.xml')
  if (($dump -join '') -match 'dumped to:') { return [xml]((AdbCall @('shell','cat','/sdcard/aster-update-check.xml')) -join '') }
 }
 throw 'Android UI did not settle for inspection.'
}
function Click-Text([string]$Text) {
 $deadline = [DateTime]::UtcNow.AddSeconds(40)
 do {
  $ui = Read-Ui
  $button = $ui.SelectSingleNode("//node[@text='$Text' and @enabled='true']")
  if ($button) {
   $bounds = [regex]::Matches($button.bounds,'\d+') | ForEach-Object { [int]$_.Value }
   AdbCall @('shell','input','tap',([int](($bounds[0]+$bounds[2])/2)).ToString(),([int](($bounds[1]+$bounds[3])/2)).ToString()) | Out-Null
   return
  }
 } while ([DateTime]::UtcNow -lt $deadline)
 throw "Installer button not found: $Text (English emulator required)"
}
AdbCall @('install','-r',"$androidRoot/app/build/outputs/apk/debug/app-debug.apk") | Out-Null
AdbCall @('install','-r',"$androidRoot/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk") | Out-Null
AdbCall @('shell','pm','grant','com.aster.vpn','android.permission.POST_NOTIFICATIONS') | Out-Null
AdbCall @('shell','appops','set','com.aster.vpn','REQUEST_INSTALL_PACKAGES','allow') | Out-Null
AdbCall @('push',"$androidRoot/app/build/outputs/apk/debug/app-debug.apk",'/data/local/tmp/aster-update.apk') | Out-Null
AdbCall @('shell','run-as','com.aster.vpn','mkdir','-p','cache') | Out-Null
AdbCall @('shell','run-as','com.aster.vpn','cp','/data/local/tmp/aster-update.apk','cache/update.apk') | Out-Null
$result = AdbCall @('shell','am','instrument','-w','-e','mode','installer','com.aster.vpn.test/com.aster.vpn.NativeChecks')
if (($result -join '') -notmatch 'PASS: download progress and cancellation') { throw "Instrumentation failed: $result" }
Click-Text 'UPDATE'
Click-Text 'OPEN'
$ui = Read-Ui
if (!$ui.SelectSingleNode("//node[@package='com.aster.vpn' and contains(@text,'ASTER')]")) { throw 'Installer Open did not display Aster VPN.' }
Write-Output 'PASS: download progress, cancellation, launcher reuse, system update installation and Open button'
