# AWG stability: Windows 1.0.8 / Android 1.0.21

## Confirmed defects

- Both SOCKS bridges expired upstream UDP flows after two minutes without a
  reply, even while the application continued sending. Refresh the receive
  deadline on outgoing packets too. The TCP control connection now owns the
  association lifetime instead of an unrelated three-minute UDP receive timer.
- Windows used the upstream netstack without releasing consumed Views and
  injected PacketBuffers. Use the existing Android adaptation, including its
  bounded queues, ownership checks and synchronized shutdown.
- Windows started the TUN reader before applying S4 padding. Apply the Android
  configuration gate so the first payload uses the configured transport offset.
- Android stopped the VPN permanently when its AWG process died and did not
  rebuild AWG after physical network changes. Debounce network changes, rebuild
  with fresh endpoint resolution and protected sockets, and retry failed recovery
  with bounded backoff. Ignore callbacks belonging to an older bridge and cancel
  pending recovery on disconnect/close.

## Verification

- Both Go module test suites, including packet ownership/shutdown tests.
- Real encrypted AWG TCP/UDP peers with nonzero S4 padding and concurrent clients.
- Regression test: a send-only flow runs beyond its receive timeout without
  reopening. Restoring the previous logic makes the test fail (three dials);
  the fixed code maintains one connection. Closing TCP still stops the workers.
- Windows: all 160 application checks and self-contained installer build.
- Android: native builds for all three ABIs; unit tests, release APK, release lint.
- Existing Android signing configuration retained; no new signing key generated.

No Android device was attached for physical network handover tests. Provider-side
outages and long-running behavior against the user's servers are not established
by these local tests.
