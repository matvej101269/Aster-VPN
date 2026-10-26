package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"github.com/amnezia-vpn/amneziawg-go/v3/conn"
	"github.com/amnezia-vpn/amneziawg-go/v3/device"
	"github.com/amnezia-vpn/amneziawg-go/v3/tun/netstack"
	"golang.org/x/crypto/curve25519"
	"io"
	"net"
	"net/netip"
	"sync"
	"testing"
	"time"
)

func pair(t *testing.T) (*netstack.Net, *netstack.Net) {
	t.Helper()
	keys := make([][]byte, 2)
	pubs := make([][]byte, 2)
	ports := make([]int, 2)
	for i := 0; i < 2; i++ {
		keys[i] = make([]byte, 32)
		rand.Read(keys[i])
		pubs[i], _ = curve25519.X25519(keys[i], curve25519.Basepoint)
		s, e := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
		if e != nil {
			t.Fatal(e)
		}
		ports[i] = s.LocalAddr().(*net.UDPAddr).Port
		s.Close()
	}
	stacks := make([]*netstack.Net, 2)
	for i := 0; i < 2; i++ {
		tun, stack, e := netstack.CreateNetTUN([]netip.Addr{netip.MustParseAddr(fmt.Sprintf("10.90.0.%d", i+1))}, nil, 1400)
		if e != nil {
			t.Fatal(e)
		}
		stacks[i] = stack
		dev := device.NewDevice(tun, conn.NewStdNetBind(), device.NewLogger(device.LogLevelSilent, ""))
		t.Cleanup(dev.Close)
		config := fmt.Sprintf("private_key=%s\nlisten_port=%d\njc=2\njmin=20\njmax=40\ns1=16\ns2=32\nh1=101\nh2=102\nh3=103\nh4=104\nreplace_peers=true\npublic_key=%s\nendpoint=127.0.0.1:%d\nallowed_ip=10.90.0.%d/32\n", hex.EncodeToString(keys[i]), ports[i], hex.EncodeToString(pubs[1-i]), ports[1-i], 2-i)
		if e = dev.IpcSet(config); e != nil {
			t.Fatal(e)
		}
		if e = dev.Up(); e != nil {
			t.Fatal(e)
		}
	}
	return stacks[0], stacks[1]
}

func authenticate(t *testing.T, c net.Conn, password string) {
	t.Helper()
	c.SetDeadline(time.Now().Add(15 * time.Second))
	c.Write([]byte{5, 1, 2})
	b := make([]byte, 2)
	io.ReadFull(c, b)
	if b[1] != 2 {
		t.Fatal("method")
	}
	auth := append([]byte{1, 5}, []byte("aster")...)
	auth = append(auth, byte(len(password)))
	auth = append(auth, []byte(password)...)
	c.Write(auth)
	io.ReadFull(c, b)
	if b[1] != 0 {
		t.Fatal("auth")
	}
}

func TestAmneziaSocksTCPAndUDP(t *testing.T) {
	a, b := pair(t)
	password := "local-test-password-1234"
	listener, e := b.ListenTCPAddrPort(netip.MustParseAddrPort("10.90.0.2:8080"))
	if e != nil {
		t.Fatal(e)
	}
	defer listener.Close()
	go func() {
		c, e := listener.Accept()
		if e == nil {
			defer c.Close()
			io.Copy(c, c)
		}
	}()
	client, server := net.Pipe()
	go serve(server, a, password)
	defer client.Close()
	authenticate(t, client, password)
	client.Write([]byte{5, 1, 0, 1, 10, 90, 0, 2, 31, 144})
	reply := make([]byte, 10)
	if _, e = io.ReadFull(client, reply); e != nil || reply[1] != 0 {
		t.Fatalf("CONNECT: %v %v", reply, e)
	}
	client.Write([]byte("encrypted-echo"))
	data := make([]byte, 14)
	if _, e = io.ReadFull(client, data); e != nil || string(data) != "encrypted-echo" {
		t.Fatalf("TCP echo: %q %v", data, e)
	}
	client.Close()
	udp, e := b.ListenUDPAddrPort(netip.MustParseAddrPort("10.90.0.2:9000"))
	if e != nil {
		t.Fatal(e)
	}
	defer udp.Close()
	go func() {
		buf := make([]byte, 1024)
		n, from, e := udp.ReadFrom(buf)
		if e == nil {
			udp.WriteTo(buf[:n], from)
		}
	}()
	client2, server2 := net.Pipe()
	go serve(server2, a, password)
	defer client2.Close()
	authenticate(t, client2, password)
	client2.Write([]byte{5, 3, 0, 1, 0, 0, 0, 0, 0, 0})
	if _, e = io.ReadFull(client2, reply); e != nil || reply[1] != 0 {
		t.Fatalf("ASSOCIATE: %v", e)
	}
	relay, e := net.DialUDP("udp4", nil, &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: int(reply[8])<<8 | int(reply[9])})
	if e != nil {
		t.Fatal(e)
	}
	defer relay.Close()
	relay.SetDeadline(time.Now().Add(15 * time.Second))
	packet := append([]byte{0, 0, 0, 1, 10, 90, 0, 2, 35, 40}, []byte("udp-through-awg")...)
	relay.Write(packet)
	buf := make([]byte, 1024)
	n, e := relay.Read(buf)
	if e != nil || string(buf[10:n]) != "udp-through-awg" {
		t.Fatalf("UDP echo failed: %v", e)
	}
}

func TestRejectUnauthenticatedSOCKS(t *testing.T) {
	c, s := net.Pipe()
	go serve(s, nil, "secret")
	defer c.Close()
	c.SetDeadline(time.Now().Add(time.Second))
	c.Write([]byte{5, 1, 0})
	b := make([]byte, 2)
	io.ReadFull(c, b)
	if b[1] != 255 {
		t.Fatal("anonymous connection accepted")
	}
}

// Keep >256 authenticated connections open concurrently, then transfer data
// through real encrypted AWG peers. This exercises the production accept loop.
func TestConcurrentClients(t *testing.T) {
	a, b := pair(t)
	upstream, err := b.ListenTCPAddrPort(netip.MustParseAddrPort("10.90.0.2:8080"))
	if err != nil {
		t.Fatal(err)
	}
	defer upstream.Close()
	go func() {
		for {
			c, err := upstream.Accept()
			if err != nil {
				return
			}
			go func() { defer c.Close(); io.Copy(c, c) }()
		}
	}()
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	defer listener.Close()
	go acceptClients(ctx, listener, a, "local-test-password-1234")
	var clients []net.Conn
	defer func() {
		for _, c := range clients {
			c.Close()
		}
	}()
	for i := 0; i < 320; i++ {
		c, err := net.DialTimeout("tcp4", listener.Addr().String(), 5*time.Second)
		if err != nil {
			t.Fatal(err)
		}
		clients = append(clients, c)
		authenticate(t, c, "local-test-password-1234")
		c.Write([]byte{5, 1, 0, 1, 10, 90, 0, 2, 31, 144})
		var reply [10]byte
		if _, err := io.ReadFull(c, reply[:]); err != nil || reply[1] != 0 {
			t.Fatalf("client %d CONNECT failed: %v", i, err)
		}
	}
	payload := bytes.Repeat([]byte("parallel-awg-data"), 4096)
	start := time.Now()
	var wg sync.WaitGroup
	for _, c := range clients {
		wg.Add(1)
		go func(c net.Conn) {
			defer wg.Done()
			c.SetDeadline(time.Now().Add(30 * time.Second))
			written := make(chan error, 1)
			go func() { _, err := c.Write(payload); written <- err }()
			reply := make([]byte, len(payload))
			_, err := io.ReadFull(c, reply)
			if err != nil || !bytes.Equal(reply, payload) {
				t.Errorf("corrupted/lost data: %v", err)
			}
			if err := <-written; err != nil {
				t.Error(err)
			}
		}(c)
	}
	wg.Wait()
	t.Logf("320 open connections; %.1f MiB echoed in %v", float64(len(payload)*len(clients))/(1024*1024), time.Since(start))
}
