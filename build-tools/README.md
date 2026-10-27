# Reproducible tools and runtime inputs

`dependencies.json` records SHA-256 and sizes of Android runtime binaries tracked
under `android/app/libs/` and `jniLibs/`, plus the portable Inno Setup archive.
`tool-downloads.json` pins official JDK 17, Gradle 8.13, Android command-line tools 19
and Go 1.27.2 downloads and SHA-256 digests. SDK packages: Android 35/build-tools 35.0.0.

`inno-7.1.0.zip` contains the unmodified portable Inno Setup files used for Windows
installers, including the original license, copyright and language files.
Inno Setup is by Jordan Russell and Martijn Laan: https://jrsoftware.org/
It is extracted into the ignored `vendor/` directory by `scripts/setup-workspace.ps1`.
SDKs, Gradle/Maven/Go caches and build output are intentionally downloaded/generated,
not committed as workspace state.
