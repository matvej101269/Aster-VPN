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
Для пересборки Android native AWG сначала выполните
`./scripts/setup-workspace.ps1 -AndroidOnly -Native` (добавляет Go и NDK r27c),
затем `./android/native/build-awgproxy.ps1`.
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

## Android 1.0.20: обновление и управление из лаунчера

Установить APK поверх прежнего `com.aster.vpn`, без удаления приложения: настройки
сохраняются. Используется прежний release keystore; перед сборкой проверить его
через `android-signing.ps1 -Mode Verify` с локальным recovery key владельца.
Новый сертификат не создавать. Windows в этом выпуске не изменён.

Лаунчер вызывает `com.aster.vpn.ControlActivity` через `startActivityForResult`
с действием `com.aster.vpn.action.CONNECT` или `com.aster.vpn.action.DISCONNECT`.
Не добавлять NEW_TASK, NEW_DOCUMENT и другие флаги, разрывающие получение результата.
Пакет вызывающего приложения берётся только из `getCallingPackage()` Android;
резервный вариант для TV — пакет `getCallingActivity()`. Extras и referrer
не используются как удостоверение. Вызовы без удостоверенного caller отклоняются.
Внешние broadcast-команды закрыты, чтобы не обходить подтверждение Activity.

Первый запрос показывает подтверждение для конкретного пакета лаунчера. При CONNECT
Aster сам запрашивает `VpnService.prepare()`, если Android ещё не разрешил VPN.
Выключение «Управление из лаунчера» отзывает сохранённые разрешения пакетов.
CONNECT при подключении ждёт его завершения, при включённом VPN ничего не меняет;
DISCONNECT отменяет подключение или отключает VPN, повторное отключение успешно.

Результат: `RESULT_OK` для завершённой команды/STATUS, `RESULT_CANCELED` для отказа,
ошибки или тайм-аута 60 секунд. Extras: `command`, `result`, `message`, `active`,
`connecting`, `state`. Коды результата: `completed`, `already_connected`,
`already_disconnected`, `status`, `unknown_caller`, `invalid_command`, `denied`,
`vpn_denied`, `no_profile`, `timeout`, `cancelled`, `error`.
Тайм-аут или закрытие ожидания не отменяют уже отправленную команду; следует
проверить состояние в Aster. Отказы, ожидание разрешения и итог пишутся в журнал Aster.
Проверка на физическом телевизоре для этого изменения не выполнялась.

## Android 1.0.22: Button Mapper

Обычный startActivity (в том числе NEW_TASK) теперь поддерживается без подмены
caller: при отсутствии Android identity пользователь подтверждает каждую команду.
Для управления одной кнопкой есть локальная настройка **Кнопка пульта (Button Mapper)**:
пользователь явно разрешает управление и создаёт случайную 256-битную bearer-ссылку.
Это самостоятельный секрет доступа, а не имя или удостоверение вызывающего пакета.
Токен хранится только в приватных SharedPreferences (backup приложения отключён),
передаётся как `astervpn://toggle?key=…`, сравнивается без раннего выхода по символам
и не записывается в журнал. Дубликаты параметров, фрагменты, неверный/отозванный ключ
отклоняются с `invalid_token`. Создание новой ссылки или выключение внешнего управления
отзывает прежний ключ. Личный ключ нельзя добавлять в Git или документацию.
Идентификация caller для постоянных пакетных разрешений осталась только Android API.
Внешний Broadcast остаётся закрытым. Исходная подпись и com.aster.vpn сохранены.
См. [настройку Button Mapper](android/LAUNCHER.md). Установка поверх прежней версии,
без удаления данных. Телевизор не использовался.
