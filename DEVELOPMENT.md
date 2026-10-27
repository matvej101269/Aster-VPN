# Продолжение разработки на другом компьютере или в другом агенте

Репозиторий: https://github.com/matvej101269/Aster-VPN

## Что теперь хранится в Git

- Исходники Windows/WPF, Android/TV, Android и Windows AWG, тесты и скрипты.
- `android/app/libs/libv2ray.aar` и `jniLibs/*/libasterawg.so` для трёх ABI.
- Соответствующие исходники LGPL-обёртки `third-party/AndroidLibXrayLite-26.10.21`;
  native AWG собирается из `android/native/awgbridge` с закреплёнными Go-зависимостями.
- Контрольные суммы зависимостей, portable Inno Setup, установщик инструментов.
- **Зашифрованная** копия исходного Android keystore и его свойств:
  `android/signing-backup.enc.json`. Сертификат подписи не заменять.

## Быстрый старт (Windows x64, PowerShell 7)

```powershell
git clone https://github.com/matvej101269/Aster-VPN.git
cd Aster-VPN
./scripts/setup-workspace.ps1      # либо -AndroidOnly
./scripts/verify-dependencies.ps1
./android/build.ps1 -Task @('testDebugUnitTest','assembleDebug')
```

Для Windows нужен .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).
JDK, Gradle, Android SDK, Go и Windows VPN-ядра ставятся скриптом из официальных
источников с проверкой сумм. На первом запуске требуется интернет для SDK и Maven/Go/NuGet.
Android native AWG можно пересобрать скриптом `android/native/build-awgproxy.ps1`
после установки Android NDK r27c в `android/tools/ndk/`.
Для Linux/macOS Android можно собрать обычным Gradle 8.13/JDK17/SDK35;
Windows WPF и Windows-установщик собираются на Windows.

## Восстановление существующей подписи Android

Файл расшифровки **не находится в публичном репозитории**. Владелец хранит его
отдельно и передаёт новому агенту локальный путь. Без этого файла можно собрать
debug APK; выпуск обновления с исходной подписью требует восстановления:

```powershell
./scripts/android-signing.ps1 -Mode Restore -KeyFile C:/secure/android-signing-recovery.key
./scripts/android-signing.ps1 -Mode Verify -KeyFile C:/secure/android-signing-recovery.key
./android/build.ps1 -Task @('testDebugUnitTest','assembleRelease','lintRelease')
```

Restore отказывается перезаписывать существующие signing-файлы.
Папка `android/signing/`, расшифрованный ключ и пароли всегда исключены из Git.
Нельзя класть recovery key рядом с зашифрованной копией в публичный Git:
это равносильно публикации исходного ключа. Новому агенту достаточно репозитория
и отдельно переданного recovery key; исходная рабочая папка больше не требуется.

## Windows

```powershell
dotnet run --project tests/AsterVPN.Tests.csproj -c Release
./scripts/build-installer.ps1
```

Профили провайдеров, подписки, HWID и файлы `.private/` не нужны для разработки
и не передаются в Git. Тесты используют искусственные данные.

## Версии и Releases

Android versionCode/versionName: `android/app/build.gradle`.
Windows Version: `src/AsterVPN.csproj`. Повышать только изменённую платформу.
Публиковать отдельными релизами `android-X.Y.Z` / `windows-X.Y.Z`, не смешивать
установщики. Хранить последние пять опубликованных версий каждой платформы;
если версий меньше пяти, сохранять все существующие без выдумывания старых версий.
См. `scripts/publish-release.ps1` и `scripts/maintain-releases.ps1`.

```powershell
./scripts/publish-release.ps1 -Platform Android -ReleaseNotes 'Изменения версии'
./scripts/maintain-releases.ps1          # предварительный просмотр
./scripts/maintain-releases.ps1 -Apply   # разделить платформы и оставить до пяти версий
```

Для публикации нужна отдельно настроенная авторизация GitHub через Git Credential
Manager/credential helper. GitHub-токены и учётные данные не хранятся в репозитории.
При очистке сохраняются исходные EXE/APK, их контрольные суммы и Git-история;
установщики не пересобираются и не переподписываются.
