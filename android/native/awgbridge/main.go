package main
/*
#include <stdlib.h>
*/
import "C"
import (
 "bufio"
 "context"
 "crypto/subtle"
 "encoding/binary"
 "encoding/json"
 "errors"
 "fmt"
 "io"
 "net"
 "net/netip"
 "sync"
 "runtime"
 "runtime/debug"
 "time"
 "github.com/amnezia-vpn/amneziawg-go/v3/conn"
 "github.com/amnezia-vpn/amneziawg-go/v3/device"
 "astervpn/awg/netstack"
)
type Config struct { Addresses []string `json:"addresses"`; DNS []string `json:"dns"`; MTU int `json:"mtu"`; UAPI string `json:"uapi"`; Debug bool `json:"debug"`; Password string `json:"password"` }
type bridge struct { device *device.Device; bind *conn.StdNetBind; listener net.Listener; cancel context.CancelFunc; clients sync.Map }
var guard sync.Mutex
var current *bridge
func stop(){ if current!=nil { current.cancel();current.listener.Close();current.clients.Range(func(key,value any)bool{key.(net.Conn).Close();return true});current.device.Close();current=nil;debug.FreeOSMemory() } }
//export asterStart
func asterStart(raw *C.char) C.int {runtime.GOMAXPROCS(2);debug.SetMemoryLimit(64<<20);guard.Lock();defer guard.Unlock();stop();var cfg Config;if json.Unmarshal([]byte(C.GoString(raw)),&cfg)!=nil||len(cfg.Password)<24||cfg.MTU<576||cfg.MTU>1280{return -1}
 parse:=func(values []string)([]netip.Addr,error){var out []netip.Addr;for _,value:=range values{a,err:=netip.ParseAddr(value);if err!=nil{return nil,err};out=append(out,a)};return out,nil}
 addresses,err:=parse(cfg.Addresses);if err!=nil||len(addresses)==0{return -2};dns,err:=parse(cfg.DNS);if err!=nil{return -2}
 tun,stack,err:=netstack.CreateNetTUN(addresses,dns,cfg.MTU);if err!=nil{return -3};bind:=conn.NewStdNetBind().(*conn.StdNetBind);level:=device.LogLevelSilent;if cfg.Debug{level=device.LogLevelVerbose};gated:=configureTUN(tun);dev:=device.NewDevice(gated,bind,device.NewLogger(level,"ASTER_AWG "));if err=dev.IpcSet(cfg.UAPI);err!=nil{dev.Close();return -4};if err=dev.Up();err!=nil{dev.Close();return -5};gated.release()
 listener,err:=net.Listen("tcp4","127.0.0.1:0");if err!=nil{dev.Close();return -6};ctx,cancel:=context.WithCancel(context.Background());b:=&bridge{device:dev,bind:bind,listener:listener,cancel:cancel};current=b
 go acceptBridge(ctx,b,stack,cfg.Password);return C.int(listener.Addr().(*net.TCPAddr).Port)
}
func acceptBridge(ctx context.Context,b *bridge,stack *netstack.Net,password string){slots:=make(chan struct{},maxClients);for{client,err:=b.listener.Accept();if err!=nil{return};select{case slots<-struct{}{}:b.clients.Store(client,true);go func(){defer func(){b.clients.Delete(client);<-slots}();serve(client,stack,password)}();default:client.Close()}}}
//export asterStop
func asterStop(){guard.Lock();defer guard.Unlock();stop()}
//export asterSocket
func asterSocket(version C.int) C.int {guard.Lock();defer guard.Unlock();if current==nil{return -1};fd:=-1;var err error;if version==4{fd,err=current.bind.PeekLookAtSocketFd4()}else{fd,err=current.bind.PeekLookAtSocketFd6()};if err!=nil{return -1};return C.int(fd)}
func main(){}
// Bound resources without rejecting ordinary browser + multi-app connection bursts.
const maxClients = 256

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

