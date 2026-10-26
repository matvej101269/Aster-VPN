# Aster VPN 0.4.5

- Forward mouse-wheel input over server lists to the subscription scroll viewer; respect the Windows scroll-lines preference.
- Decode regional-indicator country flags in subscription names. Add Russian, Bulgarian and Estonian flag colours, covering all flags in the supplied 144-profile list, alongside German, Dutch and Spanish flags.
- Bind Xray transport to both the physical interface and its local IPv4 address before TUN startup. Wait for the local SOCKS listener instead of a fixed 700 ms delay.
- Retry the initial TUN health check up to three times without restarting the tunnel. Overall connection timeout is 120 seconds; cancellation remains available.
- Use IPv4 DNS for VLESS and reject VPN-bound IPv6 inside TUN, matching the requested IPv4 operation. Existing routing exceptions remain in effect.
- Record exception type, socket error or HTTP status for failed health checks, without recording credentials or raw exception messages.

Validation: 79 automated checks passed and the WPF release build succeeded. On the running 0.4.4 tunnel, both local SOCKS and system-route HTTPS checks returned Microsoft login 200, Teams 302, ChatGPT 403 and gstatic 204. No user sign-in or private account API was accessed. These observations do not validate authenticated Teams or cloud-project workflows, and do not prove intermittent failures resolved. The updated TUN was not activated alongside the user's running clients.

Happ was left running. The installer excludes user settings, subscriptions, diagnostics and test files.
