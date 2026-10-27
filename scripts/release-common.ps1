$ReleaseRepo='matvej101269/Aster-VPN'
$ReleaseApi="https://api.github.com/repos/$ReleaseRepo"
function Get-RepoReleases($Headers){$items=Invoke-RestMethod -Uri "$ReleaseApi/releases?per_page=100" -Headers $Headers;foreach($item in $items){Write-Output $item}}
function Get-ReleaseHeaders {
 $lines="protocol=https`nhost=github.com`n`n"|git -c "safe.directory=$($root.Replace('\','/'))" credential fill
 if($LASTEXITCODE -ne 0){throw 'GitHub authentication unavailable.'}
 $password=$lines|Where-Object{$_.StartsWith('password=')}|Select-Object -First 1
 if(!$password){throw 'GitHub credential not available.'}
 return @{Authorization='Bearer '+$password.Substring(9);Accept='application/vnd.github+json';'User-Agent'='AsterVPN-releases'}
}
function Upload-ReleaseAsset($Headers,$Release,$Path,$Name){
 $expected='sha256:'+(Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()
 $assets=Invoke-RestMethod -Uri "$ReleaseApi/releases/$($Release.id)/assets" -Headers $Headers
 $existing=$assets|Where-Object name -eq $Name
 if($existing){if($existing.digest -ne $expected){throw "Existing asset differs: $Name"};return}
 $uri="https://uploads.github.com/repos/$ReleaseRepo/releases/$($Release.id)/assets?name=$([Uri]::EscapeDataString($Name))"
 $uploaded=Invoke-RestMethod -Uri $uri -Method Post -Headers $Headers -ContentType 'application/octet-stream' -InFile $Path -TimeoutSec 900
 if($uploaded.digest -ne $expected){throw "Uploaded digest mismatch: $Name"}
 Write-Output "Verified asset: $Name"
}
