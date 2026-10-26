package main

import (
	"bytes"
	"context"
	"io"
	"net"
	"testing"
	"time"
)

func TestUDPBlockedDestinationDoesNotStallOthers(t *testing.T) {
	client, server := net.Pipe()
	defer client.Close()
	defer server.Close()
	started := make(chan struct{})
	stopped := make(chan struct{})
	relayDone := make(chan struct{})
	dial := func(ctx context.Context, network, target string) (net.Conn, error) {
		if target == "10.0.0.1:9000" {
			close(started)
			<-ctx.Done()
			close(stopped)
			return nil, ctx.Err()
		}
		a, b := net.Pipe()
		go func() { defer b.Close(); io.Copy(b, b) }()
		return a, nil
	}
	go func() { relayUDPWithDial(server, server, dial); close(relayDone) }()
	client.SetReadDeadline(time.Now().Add(time.Second))
	var reply [10]byte
	if _, err := io.ReadFull(client, reply[:]); err != nil {
		t.Fatal(err)
	}
	socket, err := net.DialUDP("udp4", nil, &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: int(reply[8])<<8 | int(reply[9])})
	if err != nil {
		t.Fatal(err)
	}
	defer socket.Close()
	socket.SetDeadline(time.Now().Add(2 * time.Second))
	blocked := []byte{0, 0, 0, 1, 10, 0, 0, 1, 35, 40, 42}
	if _, err = socket.Write(blocked); err != nil {
		t.Fatal(err)
	}
	select {
	case <-started:
	case <-time.After(time.Second):
		t.Fatal("blocked dial not started")
	}
	// Saturating one pending destination must not occupy another destination's
	// queue or block receive, even while its dial remains unfinished.
	largeBlocked := append(append([]byte(nil), blocked[:10]...), make([]byte, 63000)...)
	for i := 0; i < 64; i++ {
		if _, err = socket.Write(largeBlocked); err != nil {
			t.Fatal(err)
		}
	}
	healthy := []byte{0, 0, 0, 1, 10, 0, 0, 2, 35, 40, 1, 2, 3, 4}
	start := time.Now()
	if _, err = socket.Write(healthy); err != nil {
		t.Fatal(err)
	}
	var response [100]byte
	n, err := socket.Read(response[:])
	if err != nil || !bytes.Equal(response[:n], healthy) {
		t.Fatalf("healthy UDP blocked/corrupted: %v", err)
	}
	t.Logf("healthy destination replied in %v while another dial remained blocked", time.Since(start))
	client.Close()
	select {
	case <-stopped:
	case <-time.After(time.Second):
		t.Fatal("association close did not cancel pending dial")
	}
	select {
	case <-relayDone:
	case <-time.After(time.Second):
		t.Fatal("relay workers did not terminate")
	}
}
