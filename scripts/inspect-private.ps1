$ErrorActionPreference = 'Stop'
$cipher = [IO.File]::ReadAllBytes((Join-Path $PWD '.private/subscription.bin'))
$body = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
if ($body -notmatch '://') {
 $s = $body.Trim().Replace('-','+').Replace('_','/')
 $body = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($s.PadRight([int]([Math]::Ceiling($s.Length/4)*4),'=')))
}
function Shape($obj, $prefix, $depth) {
 if ($depth -gt 6) { return }
 if ($obj -is [pscustomobject]) {
  Write-Output ($prefix + ': ' + ($obj.PSObject.Properties.Name -join ','))
  foreach ($p in $obj.PSObject.Properties) {
   if ($p.Value -is [pscustomobject] -or $p.Value -is [array]) { Shape $p.Value ($prefix+'.'+$p.Name) ($depth+1) }
   elseif ($p.Value -is [string] -and $p.Value.TrimStart().StartsWith('{')) { try { Shape ($p.Value|ConvertFrom-Json) ($prefix+'.'+$p.Name) ($depth+1) } catch {} }
   elseif ($p.Value -is [string] -and $p.Value.Contains('[Interface]')) { Write-Output ('INI keys: '+(([regex]::Matches($p.Value,'(?m)^\s*([A-Za-z0-9]+)\s*=')|ForEach-Object {$_.Groups[1].Value}) -join ',')) }
  }
 } elseif ($obj -is [array]) { foreach($item in $obj) { Shape $item $prefix ($depth+1) } }
}
foreach ($line in ($body -split '\r?\n' | Where-Object { $_ -like 'vpn://*' })) {
 $s = ($line.Substring(6) -split '#')[0].Replace('-','+').Replace('_','/')
 $bytes = [Convert]::FromBase64String($s.PadRight([int]([Math]::Ceiling($s.Length/4)*4),'='))
 Write-Output ('Payload magic: '+[Convert]::ToHexString($bytes[0..3]))
 if ($bytes[0] -eq 91) { $conf = [Text.Encoding]::UTF8.GetString($bytes); Write-Output ('INI keys: '+(([regex]::Matches($conf,'(?m)^\s*([A-Za-z0-9]+)\s*=')|ForEach-Object {$_.Groups[1].Value}) -join ',')); continue }
 if ($bytes[0] -eq 123) { $json = [Text.Encoding]::UTF8.GetString($bytes) }
 else {
  $offset = if ($bytes[0] -eq 120) {0} else {4}
  $ms = [IO.MemoryStream]::new($bytes,$offset,$bytes.Length-$offset)
  $zs = [IO.Compression.ZLibStream]::new($ms,[IO.Compression.CompressionMode]::Decompress)
  $reader = [IO.StreamReader]::new($zs)
  try { $json = $reader.ReadToEnd() } catch { Write-Output 'Decode failed'; continue }
 }
 Shape ($json|ConvertFrom-Json) 'root' 0
}
