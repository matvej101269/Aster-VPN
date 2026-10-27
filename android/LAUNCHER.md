# Управление из ТВ-лаунчера

Включите **Настройки → Управление из лаунчера**. Один раз подключите VPN обычной
кнопкой, чтобы Android выдал системное разрешение. Команды используют последний
успешно подключённый сервер, как плитка шторки.

## Ссылки connect/disconnect/toggle

| Действие | URI |
|---|---|
| Переключить | `astervpn://toggle` |
| Подключить | `astervpn://connect` (также `astervpn://open`) |
| Отключить | `astervpn://disconnect` (также `astervpn://close`) |
| Показать состояние | `astervpn://status` |

Схема `aster://` поддерживает те же команды.

```java
startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("astervpn://toggle"))
    .setPackage("com.aster.vpn").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
```

## Explicit Intent и Broadcast

| Назначение | Значение |
|---|---|
| Package | `com.aster.vpn` |
| Activity | `com.aster.vpn.ControlActivity` |
| Broadcast receiver | `com.aster.vpn.ControlReceiver` |
| Подключить | `com.aster.vpn.action.CONNECT` |
| Отключить | `com.aster.vpn.action.DISCONNECT` |
| Переключить | `com.aster.vpn.action.TOGGLE` |
| Статус (Activity) | `com.aster.vpn.action.STATUS` |

```java
sendBroadcast(new Intent("com.aster.vpn.action.TOGGLE")
    .setComponent(new ComponentName("com.aster.vpn", "com.aster.vpn.ControlReceiver")));
```

Если нужно запросить VPN-разрешение, используйте Activity/URI. Broadcast работает
после первоначальной настройки; Android может запрещать запуск сервиса из фона.
DISCONNECT не включает отключённый VPN, CONNECT не разрывает работающее соединение.
TOGGLE во время подключения отменяет подключение.

```text
adb shell am start -a android.intent.action.VIEW -d astervpn://toggle com.aster.vpn
adb shell am broadcast -n com.aster.vpn/.ControlReceiver -a com.aster.vpn.action.DISCONNECT
```

## Локальная сеть

«Локальная сеть в обход VPN» исключает из IPv4 TUN частные сети 10/8, 172.16/12,
192.168/16, link-local 169.254/16, multicast для обнаружения устройств и broadcast.
Работает на Android 8+ с Xray и AWG. После изменения переподключите VPN.
Прежняя политика блокировки IPv6 не меняется.
