param([ValidateSet('Backup','Restore','Verify')][string]$Mode='Verify',[Parameter(Mandatory)][string]$KeyFile)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$backup=Join-Path $root 'android/signing-backup.enc.json'
$signing=Join-Path $root 'android/signing'
if($Mode -eq 'Backup' -and !(Test-Path -LiteralPath $KeyFile)){
 $key=[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
 [IO.File]::WriteAllText([IO.Path]::GetFullPath($KeyFile),[Convert]::ToBase64String($key))
}else{$key=[Convert]::FromBase64String([IO.File]::ReadAllText([IO.Path]::GetFullPath($KeyFile)).Trim())}
if($key.Length -ne 32){throw 'Recovery key must contain 32 base64-encoded bytes.'}
$aes=[Security.Cryptography.AesGcm]::new($key,16)
try{
 if($Mode -eq 'Backup'){
  $payload=@{keystore=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $signing 'aster-release.p12')));properties=[IO.File]::ReadAllText((Join-Path $signing 'release.properties'))}|ConvertTo-Json -Compress
  $plain=[Text.Encoding]::UTF8.GetBytes($payload);$nonce=[Security.Cryptography.RandomNumberGenerator]::GetBytes(12);$cipher=[byte[]]::new($plain.Length);$tag=[byte[]]::new(16)
  $aes.Encrypt($nonce,$plain,$cipher,$tag)
  @{format='aster-signing-aes256-gcm-v1';nonce=[Convert]::ToBase64String($nonce);tag=[Convert]::ToBase64String($tag);ciphertext=[Convert]::ToBase64String($cipher)}|ConvertTo-Json|Set-Content -LiteralPath $backup -Encoding utf8
  Write-Output 'Encrypted Android signing backup created. Keep the recovery key outside Git.'
 }else{
  $data=Get-Content -LiteralPath $backup -Raw|ConvertFrom-Json
  if($data.format -ne 'aster-signing-aes256-gcm-v1'){throw 'Unsupported backup format.'}
  $cipher=[Convert]::FromBase64String($data.ciphertext);$plain=[byte[]]::new($cipher.Length)
  $aes.Decrypt([Convert]::FromBase64String($data.nonce),$cipher,[Convert]::FromBase64String($data.tag),$plain)
  $payload=[Text.Encoding]::UTF8.GetString($plain)|ConvertFrom-Json
  $keystore=[Convert]::FromBase64String($payload.keystore)
  if($Mode -eq 'Restore'){
   if((Test-Path (Join-Path $signing 'aster-release.p12')) -or (Test-Path (Join-Path $signing 'release.properties'))){throw 'Signing files already exist; refusing to overwrite.'}
   New-Item -ItemType Directory -Path $signing -Force|Out-Null
   [IO.File]::WriteAllBytes((Join-Path $signing 'aster-release.p12'),$keystore)
   [IO.File]::WriteAllText((Join-Path $signing 'release.properties'),$payload.properties)
   Write-Output 'Android signing files restored into the Git-ignored signing directory.'
  }else{
   if(!(Test-Path (Join-Path $signing 'aster-release.p12'))){throw 'Local keystore missing; use Restore.'}
   $actual=[Security.Cryptography.SHA256]::HashData($keystore)
   $expected=[Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes((Join-Path $signing 'aster-release.p12')))
   if(![Security.Cryptography.CryptographicOperations]::FixedTimeEquals($actual,$expected) -or $payload.properties -ne [IO.File]::ReadAllText((Join-Path $signing 'release.properties'))){throw 'Signing backup differs from local signing files.'}
   Write-Output 'PASS: encrypted backup restores the exact existing keystore and signing properties.'
  }
 }
}finally{$aes.Dispose();[Security.Cryptography.CryptographicOperations]::ZeroMemory($key);if($plain){[Security.Cryptography.CryptographicOperations]::ZeroMemory($plain)}}
