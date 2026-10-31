# Управление из ТВ-лаунчера (Android 1.0.20+)

Лаунчер должен запускать экспортированную Activity из собственной Activity через
`startActivityForResult`. Android удостоверяет пакет вызывающего приложения.
Extras с именем пакета и referrer не используются для выдачи доступа.
`startActivity`, NEW_TASK и adb shell не обеспечивают этот контракт и отклоняются.

```java
Intent command = new Intent("com.aster.vpn.action.CONNECT")
    .setComponent(new ComponentName("com.aster.vpn", "com.aster.vpn.ControlActivity"));
startActivityForResult(command, 100);
```

При первом запросе пользователь подтверждает доступ конкретному пакету лаунчера.
При необходимости Aster показывает системное разрешение VPN. Заранее подключать
VPN вручную не требуется, но подписка и выбранный сервер должны быть настроены.
Выключение **Настройки → Управление из лаунчера** отзывает все разрешения лаунчеров.

| Команда | Действие Intent | URI |
|---|---|---|
| Подключить | `com.aster.vpn.action.CONNECT` | `astervpn://connect` |
| Отключить | `com.aster.vpn.action.DISCONNECT` | `astervpn://disconnect` |
| Переключить | `com.aster.vpn.action.TOGGLE` | `astervpn://toggle` |
| Состояние | `com.aster.vpn.action.STATUS` | `astervpn://status` |

URI также запускайте через `startActivityForResult` с пакетом `com.aster.vpn`.
Схема `aster://` и псевдонимы `open`/`close` сохранены. Внешний Broadcast закрыт,
так как не позволяет использовать проверку вызывающей Activity.

CONNECT не перезапускает работающий VPN и ждёт уже начатого подключения.
DISCONNECT отключает VPN, в том числе отменяет подключение; повторная команда успешна.
TOGGLE при подключении отменяет его. STATUS возвращает состояние без открытия UI.

В `onActivityResult` проверьте `RESULT_OK` или `RESULT_CANCELED`, затем extras:
`result` (код), `message` (объяснение), `command`, `active`, `connecting`, `state`.
Успех возвращается после достижения нужного состояния, а не сразу после отправки.
Ожидание ограничено 60 секундами. При тайм-ауте или закрытии Activity команда может
продолжаться: проверьте STATUS. Отказы и итоги доступны в журнале Aster.
Полный список кодов — [DEVELOPMENT.md](../DEVELOPMENT.md#android-1020-обновление-и-управление-из-лаунчера).

## Обновление

Устанавливайте 1.0.20 поверх прежней версии без удаления приложения. Пакет
`com.aster.vpn` и исходная подпись сохранены, настройки остаются на месте.

## Локальная сеть

«Локальная сеть в обход VPN» исключает из IPv4 TUN частные сети 10/8, 172.16/12,
192.168/16, link-local 169.254/16, multicast для обнаружения устройств и broadcast.
Работает на Android 8+ с Xray и AWG. После изменения переподключите VPN.
Прежняя политика блокировки IPv6 не меняется.
