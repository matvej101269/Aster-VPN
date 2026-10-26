param([string[]]$Abi=@('arm64-v8a','armeabi-v7a','x86_64'))
$ErrorActionPreference='Stop'
$androidRoot=Split-Path $PSScriptRoot -Parent
$digest=[Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($androidRoot));$suffix=([BitConverter]::ToString($digest)).Replace('-','').Substring(0,12)
$buildRoot=Join-Path $env:TEMP "aster-android-$suffix"
if(!(Test-Path -LiteralPath $buildRoot)){New-Item -ItemType Junction -Path $buildRoot -Target $androidRoot|Out-Null}
$link=Get-Item -LiteralPath $buildRoot;if($link.LinkType-ne 'Junction'-or $link.Target-ne $androidRoot){throw 'Invalid build junction'}
$env:GOMODCACHE=Join-Path (Split-Path $androidRoot -Parent) 'vendor/gomod';$env:GOCACHE=Join-Path (Split-Path $androidRoot -Parent) 'vendor/gocache';$env:GOOS='android';$env:CGO_ENABLED='1'
$go=Join-Path (Split-Path $androidRoot -Parent) 'vendor/go/bin/go.exe'
$toolchain=Join-Path $buildRoot 'tools/ndk/android-ndk-r27c/toolchains/llvm/prebuilt/windows-x86_64/bin'
foreach($arch in $Abi){switch($arch){'arm64-v8a'{$env:GOARCH='arm64';$cc='aarch64-linux-android26-clang.cmd'}'armeabi-v7a'{$env:GOARCH='arm';$env:GOARM='7';$cc='armv7a-linux-androideabi26-clang.cmd'}'x86_64'{$env:GOARCH='amd64';$cc='x86_64-linux-android26-clang.cmd'}default{throw 'Unknown ABI'}};$env:CC=Join-Path $toolchain $cc;$out=Join-Path $buildRoot "app/src/main/jniLibs/$arch/libasterawg.so";New-Item -ItemType Directory (Split-Path $out) -Force|Out-Null;& $go -C "$buildRoot/native/awgbridge" build -trimpath -buildmode=c-shared -ldflags '-s -w -extldflags=-Wl,-z,max-page-size=16384' -o $out .;if($LASTEXITCODE-ne 0){throw "Native AWG build failed: $arch"};"Built $arch"}
