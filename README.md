# Aster VPN

Нативные VPN-клиенты для **Windows** и **Android / Android TV**.
Windows: WPF/.NET 8, sing-box, Xray и AmneziaWG. Android: Java, Xray и AmneziaWG.

## Скачать

| Клиент | Версия | Установщик |
|---|---|---|
| Windows x64 | 1.0.7 | [Windows Release](https://github.com/matvej101269/Aster-VPN/releases/tag/windows-1.0.7) |
| Android / Android TV | 1.0.19 | [Android Release](https://github.com/matvej101269/Aster-VPN/releases/tag/android-1.0.19) |

Релизы платформ разделены; сохраняются до пяти последних версий каждой.
Клиенты автоматически предлагают обновления, показывают прогресс загрузки,
проверяют SHA-256. Android также проверяет подпись APK.

## Документация

- [Продолжить разработку в другом агенте / собрать с нуля](DEVELOPMENT.md)
- [Инструкция и история Windows](docs/windows.md)
- [Инструкция и история Android](android/README.md)
- [Команды для ТВ-лаунчера](android/LAUNCHER.md)
- [Правила обновлений](UPDATES.md)
- [Сторонние компоненты](THIRD-PARTY-NOTICES.md)
- [Инструкции для агентов](AGENTS.md)

## Сборка

На Windows x64 с PowerShell 7:

```powershell
./scripts/setup-workspace.ps1
./android/build.ps1 -Task @('testDebugUnitTest','assembleRelease','lintRelease')
dotnet run --project tests/AsterVPN.Tests.csproj -c Release
./scripts/build-installer.ps1
```

Для Android-обновлений восстановите исходную подпись по [инструкции](DEVELOPMENT.md).
Зашифрованная копия включена в Git; ключ расшифровки владелец передаёт отдельно.
Не создавайте новый keystore для обновлений.

## Структура

`src/` — Windows, `android/app/` — Android/TV, `awg/` и `android/native/` — AWG,
`scripts/` — сборка и публикация, `tests/` и `android/checks/` — проверки,
`build-tools/` — контрольные суммы/установка инструментов,
`third-party/` — соответствующие исходники сторонних обёрток.

Локальные SDK, кеши, результаты сборки, приватные подписки и незашифрованные
ключи подписи исключены из Git. Для первого восстановления инструментов нужен интернет.