package main

import (
	"bytes"
	"context"
	"io"
	"net"
	"sync"
	"time"

	"astervpn/awg/netstack"
)

type udpDial func(context.Context, string, string) (net.Conn, error)
type udpFlow struct {
	packets     chan []byte
	header      []byte
	queuedBytes int // protected by the association mutex
}

func relayUDP(client net.Conn, control io.Reader, stack *netstack.Net) {
	relayUDPWithDial(client, control, stack.DialContext)
}

// Each destination owns a bounded queue and worker. Slow DNS/dials/writes must
// never stop the association from receiving packets for other destinations.
func relayUDPWithDial(client net.Conn, control io.Reader, dial udpDial) {
	relayUDPWithIdle(client, control, dial, 2*time.Minute)
}

func relayUDPWithIdle(client net.Conn, control io.Reader, dial udpDial, idle time.Duration) {
	relay, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		return
	}
	defer relay.Close()
	_ = relay.SetReadBuffer(4 << 20)
	_ = relay.SetWriteBuffer(4 << 20)
	port := relay.LocalAddr().(*net.UDPAddr).Port
	if _, err := client.Write([]byte{5, 0, 0, 1, 127, 0, 0, 1, byte(port >> 8), byte(port)}); err != nil {
		return
	}
	client.SetDeadline(time.Time{})
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	go func() { io.Copy(io.Discard, control); cancel(); relay.Close() }()
	var mu sync.Mutex
	flows := map[string]*udpFlow{}
	queuedBytes := 0
	var workers sync.WaitGroup
	// Shutdown wakes every pending dial/worker, closes upstream sockets and waits
	// for cleanup, so a closed SOCKS association cannot leave relay workers behind.
	defer func() { cancel(); workers.Wait() }()
	var source *net.UDPAddr
	buf := make([]byte, 65535)
	for {
		// The authenticated TCP control connection owns the association lifetime.
		n, from, err := relay.ReadFromUDP(buf)
		if err != nil {
			return
		}
		if !from.IP.IsLoopback() || n < 4 || buf[0] != 0 || buf[1] != 0 || buf[2] != 0 {
			continue
		}
		if source == nil {
			source = from
		}
		if source.String() != from.String() {
			continue
		}
		reader := bytes.NewReader(buf[3:n])
		target, header, err := readAddress(reader)
		if err != nil {
			continue
		}
		payload := buf[n-reader.Len() : n]
		mu.Lock()
		flow := flows[target]
		if flow == nil && len(flows) < 128 {
			flow = &udpFlow{packets: make(chan []byte, 256), header: header}
			flows[target] = flow
			workers.Add(1)
			go func(key string, f *udpFlow, destination *net.UDPAddr) {
				defer workers.Done()
				defer func() {
					mu.Lock()
					delete(flows, key)
					for {
						select {
						case p := <-f.packets:
							queuedBytes -= len(p)
							f.queuedBytes -= len(p)
						default:
							mu.Unlock()
							return
						}
					}
				}()
				dialCtx, stopDial := context.WithTimeout(ctx, 10*time.Second)
				upstream, err := dial(dialCtx, "udp", key)
				stopDial()
				if err != nil {
					return
				}
				defer upstream.Close()
				flowCtx, stopFlow := context.WithCancel(ctx)
				defer stopFlow()
				readDone := make(chan struct{})
				go func() {
					defer close(readDone)
					defer stopFlow()
					packet := make([]byte, 65535)
					offset := 3 + len(f.header)
					copy(packet[3:], f.header)
					for {
						upstream.SetReadDeadline(time.Now().Add(idle))
						n, err := upstream.Read(packet[offset:])
						if err != nil {
							return
						}
						if _, err = relay.WriteToUDP(packet[:offset+n], destination); err != nil {
							return
						}
					}
				}()
				defer func() { upstream.Close(); <-readDone }()
				for {
					select {
					case <-flowCtx.Done():
						return
					case p := <-f.packets:
						// Outgoing traffic also keeps a one-way UDP flow alive.
						upstream.SetReadDeadline(time.Now().Add(idle))
						upstream.SetWriteDeadline(time.Now().Add(time.Second))
						_, err := upstream.Write(p)
						mu.Lock()
						queuedBytes -= len(p)
						f.queuedBytes -= len(p)
						mu.Unlock()
						if err != nil {
							return
						}
					}
				}
			}(target, flow, source)
		}
		// Cap queued payload across all flows, not just each destination. UDP drops
		// under overload let QUIC adapt without unbounded memory or latency growth.
		if flow != nil && len(flow.packets) < cap(flow.packets) && flow.queuedBytes+len(payload) <= 256<<10 && queuedBytes+len(payload) <= 2<<20 {
			select {
			case flow.packets <- append([]byte(nil), payload...):
				queuedBytes += len(payload)
				flow.queuedBytes += len(payload)
			default:
			}
		}
		mu.Unlock()
	}
}
