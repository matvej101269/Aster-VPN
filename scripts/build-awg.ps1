$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$go = Join-Path $root 'vendor/go/bin/go.exe'
if (!(Test-Path $go)) { $go = (Get-Command go -ErrorAction Stop).Source }
$env:GOMODCACHE = Join-Path $root 'vendor/gomod'
$env:GOCACHE = Join-Path $root 'vendor/gocache'
& $go -C (Join-Path $root 'awg') test -timeout 60s ./...
if ($LASTEXITCODE -ne 0) { throw 'AWG tests failed' }
& $go -C (Join-Path $root 'awg') build -trimpath -ldflags '-s -w' -o (Join-Path $root 'vendor/aster-awg.exe') .
if ($LASTEXITCODE -ne 0) { throw 'AWG build failed' }
