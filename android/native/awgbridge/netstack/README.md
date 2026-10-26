Adapted from AmneziaWG Go v3.1.20260828 tun/netstack/tun.go (MIT, original header retained).
Local changes: release reference-counted Views and PacketBuffers after consuming/injecting; bound both outbound queues to 64 packets; synchronize notifier shutdown with a cancellation channel; close and wait for stack workers; drain remaining owned Views. No wire protocol changes.
