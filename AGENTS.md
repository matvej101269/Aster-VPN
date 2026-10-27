# Working on Aster VPN

- Отвечай пользователю кратко и по делу.
- Upload authorized changes to GitHub unless the user asks otherwise.
- Keep Android and Windows releases separate: tags `android-X.Y.Z` and `windows-X.Y.Z`, titles `Android X.Y.Z` and `Windows X.Y.Z`. Newest versions first; retain the latest five per platform. Do not combine installers for different platforms in a release.
- Read `DEVELOPMENT.md` before building. Windows code is in `src/`; Android in `android/app/`; native Android AWG in `android/native/awgbridge/`; Windows AWG in `awg/`.
- Never publish `.private/`, plaintext signing keys/passwords, provider subscriptions, vaults, local SDKs/caches or runtime/user configuration. Android signing backup in Git is encrypted; the user supplies the recovery key separately.
- Preserve the existing Android signing certificate. Never generate a replacement signing key for a normal update.
- Build and run checks relevant to the change. Android: `./android/build.ps1 -Task @('testDebugUnitTest','assembleRelease','lintRelease')`. Windows: `dotnet run --project tests/AsterVPN.Tests.csproj -c Release` and `./scripts/build-installer.ps1`.
- Increase Android versionCode/versionName and/or Windows project Version only for changed platforms. Publish only tested output files and verify uploaded SHA-256 digests.
- For release publishing use `scripts/publish-release.ps1`; for repository retention/splitting use the maintenance script with its preview first. Do not delete source history or change existing signed binaries during release cleanup.
