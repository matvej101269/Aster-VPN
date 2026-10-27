$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($entry in (Get-Content (Join-Path $root 'build-tools/dependencies.json') -Raw|ConvertFrom-Json)){
 $path=Join-Path $root $entry.path
 if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $entry.sha256){throw "Missing or modified dependency: $($entry.path)"}
}
Write-Output 'PASS: tracked Android runtime libraries and installer tools match the dependency manifest.'
