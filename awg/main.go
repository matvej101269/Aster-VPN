// Aster AWG bridge. Protocol implementation is the official amneziawg-go.
// Only authenticated loopback SOCKS5 is exposed; no system routes are changed here.
package main

import (
	"bufio"
	"context"
	"crypto/subtle"
	"encoding/binary"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"net"
	"net/netip"
	"os"
	"os/signal"
	"time"

	"astervpn/awg/netstack"
	"github.com/amnezia-vpn/amneziawg-go/v3/conn"
	"github.com/amnezia-vpn/amneziawg-go/v3/device"
)

type Config struct {
	Addresses []string `json:"addresses"`
	DNS       []string `json:"dns"`
	MTU       int      `json:"mtu"`
	UAPI      string   `json:"uapi"`
	Port      int      `json:"port"`
	Password  string   `json:"password"`
}

func main() {
	path := flag.String("config", "", "private runtime JSON")
	check := flag.Bool("check", false, "validate configuration without connecting")
	flag.Parse()
	if err := run(*path, *check); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run(path string, check bool) error {
	data, err := os.ReadFile(path)
	if err != nil {
		return errors.New("AWG: cannot read private configuration")
	}
	var cfg Config
	if json.Unmarshal(data, &cfg) != nil || cfg.Port < 1 || cfg.Port > 65535 || len(cfg.Password) < 16 {
		return errors.New("AWG: invalid bridge configuration")
	}
	parse := func(values []string) ([]netip.Addr, error) {
		result := make([]netip.Addr, 0, len(values))
		for _, s := range values {
			a, err := netip.ParseAddr(s)
			if err != nil {
				return nil, errors.New("AWG: invalid interface or DNS address")
			}
			result = append(result, a)
		}
		return result, nil
	}
	addresses, err := parse(cfg.Addresses)
	if err != nil {
		return err
	}
	dns, err := parse(cfg.DNS)
	if err != nil {
		return err
	}
	if len(addresses) == 0 || cfg.MTU < 576 || cfg.MTU > 9000 {
		return errors.New("AWG: invalid address/MTU")
	}
	tun, stack, err := netstack.CreateNetTUN(addresses, dns, cfg.MTU)
	if err != nil {
		return errors.New("AWG: netstack initialization failed")
	}
	// WinRingBind (Windows RIO) stalled streams in repeated 320-client tests.
	// StdNetBind keeps the official protocol core and uses buffered UDP sockets.
	gated := configureTUN(tun)
	dev := device.NewDevice(gated, conn.NewStdNetBind(), device.NewLogger(device.LogLevelSilent, ""))
	defer dev.Close()
	// UAPI endpoint must be an IP:port, resolved by the controller before TUN startup.
	if err := dev.IpcSet(cfg.UAPI); err != nil {
		return errors.New("AWG: core rejected keys or obfuscation parameters")
	}
	if check {
		fmt.Println("VALID")
		return nil
	}
	if err := dev.Up(); err != nil {
		return errors.New("AWG: device startup failed")
	}
	gated.release()
	listener, err := net.Listen("tcp4", fmt.Sprintf("127.0.0.1:%d", cfg.Port))
	if err != nil {
		return errors.New("AWG: loopback port unavailable")
	}
	defer listener.Close()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	go func() { <-ctx.Done(); listener.Close() }()
	fmt.Println("READY")
	return acceptClients(ctx, listener, stack, cfg.Password)
}

// Bound resources without rejecting ordinary browser + multi-app connection bursts.
const maxClients = 2048

func acceptClients(ctx context.Context, listener net.Listener, stack *netstack.Net, password string) error {
	slots := make(chan struct{}, maxClients)
	for {
		client, err := listener.Accept()
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return errors.New("AWG: listener failed")
		}
		select {
		case slots <- struct{}{}:
			go func() { defer func() { <-slots }(); serve(client, stack, password) }()
		default:
			client.Close()
		}
	}
}

func readAddress(r io.Reader) (string, []byte, error) {
	var kind [1]byte
	if _, err := io.ReadFull(r, kind[:]); err != nil {
		return "", nil, err
	}
	header := []byte{kind[0]}
	size := 0
	switch kind[0] {
	case 1:
		size = 4
	case 4:
		size = 16
	case 3:
		var n [1]byte
		if _, err := io.ReadFull(r, n[:]); err != nil {
			return "", nil, err
		}
		size = int(n[0])
		header = append(header, n[0])
		if size == 0 {
			return "", nil, errors.New("empty domain")
		}
	default:
		return "", nil, errors.New("invalid address type")
	}
	buf := make([]byte, size+2)
	if _, err := io.ReadFull(r, buf); err != nil {
		return "", nil, err
	}
	header = append(header, buf...)
	host := string(buf[:size])
	if kind[0] != 3 {
		host = net.IP(buf[:size]).String()
	}
	port := binary.BigEndian.Uint16(buf[size:])
	if port == 0 {
		return "", nil, errors.New("zero port")
	}
	return net.JoinHostPort(host, fmt.Sprint(port)), header, nil
}

func serve(client net.Conn, stack *netstack.Net, password string) {
	defer client.Close()
	client.SetDeadline(time.Now().Add(15 * time.Second))
	r := bufio.NewReader(client)
	var greeting [2]byte
	if _, err := io.ReadFull(r, greeting[:]); err != nil || greeting[0] != 5 {
		return
	}
	methods := make([]byte, int(greeting[1]))
	if _, err := io.ReadFull(r, methods); err != nil {
		return
	}
	supports := false
	for _, m := range methods {
		if m == 2 {
			supports = true
		}
	}
	if !supports {
		client.Write([]byte{5, 255})
		return
	}
	client.Write([]byte{5, 2})
	var auth [2]byte
	if _, err := io.ReadFull(r, auth[:]); err != nil || auth[0] != 1 {
		return
	}
	user := make([]byte, int(auth[1]))
	if _, err := io.ReadFull(r, user); err != nil {
		return
	}
	n, err := r.ReadByte()
	if err != nil {
		return
	}
	secret := make([]byte, int(n))
	if _, err = io.ReadFull(r, secret); err != nil {
		return
	}
	if string(user) != "aster" || subtle.ConstantTimeCompare(secret, []byte(password)) != 1 {
		client.Write([]byte{1, 1})
		return
	}
	client.Write([]byte{1, 0})
	var request [3]byte
	if _, err = io.ReadFull(r, request[:]); err != nil || request[0] != 5 || request[2] != 0 {
		return
	}
	// UDP ASSOCIATE permits a zero destination port.
	if request[1] == 3 {
		if err := discardAssociateAddress(r); err != nil {
			return
		}
		relayUDP(client, r, stack)
		return
	}
	target, _, err := readAddress(r)
	if err != nil {
		return
	}
	if request[1] != 1 {
		client.Write([]byte{5, 7, 0, 1, 0, 0, 0, 0, 0, 0})
		return
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	upstream, err := stack.DialContext(ctx, "tcp", target)
	if err != nil {
		client.Write([]byte{5, 5, 0, 1, 0, 0, 0, 0, 0, 0})
		return
	}
	defer upstream.Close()
	client.Write([]byte{5, 0, 0, 1, 127, 0, 0, 1, 0, 0})
	client.SetDeadline(time.Time{})
	done := make(chan struct{})
	go func() {
		io.Copy(upstream, r)
		if c, ok := upstream.(interface{ CloseWrite() error }); ok {
			c.CloseWrite()
		}
		close(done)
	}()
	io.Copy(client, upstream)
	client.Close()
	upstream.Close()
	<-done
}

func discardAssociateAddress(r io.Reader) error {
	var k [1]byte
	if _, err := io.ReadFull(r, k[:]); err != nil {
		return err
	}
	size := 0
	switch k[0] {
	case 1:
		size = 4
	case 4:
		size = 16
	case 3:
		var n [1]byte
		if _, err := io.ReadFull(r, n[:]); err != nil {
			return err
		}
		size = int(n[0])
	default:
		return errors.New("address")
	}
	_, err := io.CopyN(io.Discard, r, int64(size+2))
	return err
}
