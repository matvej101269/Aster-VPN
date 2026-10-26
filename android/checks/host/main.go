package main
import("crypto/ecdh";"encoding/base64";"encoding/hex";"fmt";"net";"net/http";"net/netip";"os";"bytes";"io";"strings";"time";"github.com/amnezia-vpn/amneziawg-go/v3/conn";"github.com/amnezia-vpn/amneziawg-go/v3/device";"github.com/amnezia-vpn/amneziawg-go/v3/tun/netstack")
func main(){
 client:=make([]byte,32);server:=make([]byte,32);for i:=0;i<32;i++{client[i]=byte(i+1);server[i]=byte(32-i)};ck,_:=ecdh.X25519().NewPrivateKey(client);sk,_:=ecdh.X25519().NewPrivateKey(server)
 fmt.Println("SERVER_PUBLIC="+base64.StdEncoding.EncodeToString(sk.PublicKey().Bytes()))
 if len(os.Args)>1&&os.Args[1]=="keys"{return}
 tun,stack,err:=netstack.CreateNetTUN([]netip.Addr{netip.MustParseAddr("10.8.0.1"),netip.MustParseAddr("1.1.1.1"),netip.MustParseAddr("8.8.8.8")},nil,1280);if err!=nil{panic(err)}
 dev:=device.NewDevice(tun,conn.NewStdNetBind(),device.NewLogger(device.LogLevelVerbose,"TEST "));defer dev.Close()
 cfg:="private_key="+hex.EncodeToString(server)+"\nlisten_port=17902\njc=4\njmin=40\njmax=70\ns1=16\ns2=16\ns3=16\ns4=16\nh1=1\nh2=2\nh3=3\nh4=4\nheader_protection_key="+hex.EncodeToString(client)+"\ncontent_padding_addition=16-64\nrekey_after_time=120-180\nrekey_timeout=4-8\nreject_after_time=200-300\nkeepalive_timeout=10-20\nmax_handshake_attempts=10-20\nrandom_trailers=true\ndisable_cookies=false\nreplace_peers=true\npublic_key="+hex.EncodeToString(ck.PublicKey().Bytes())+"\nreplace_allowed_ips=true\nallowed_ip=10.8.0.2/32\n"
 if os.Getenv("ASTER_FAST_REKEY")=="1"{cfg=strings.ReplaceAll(cfg,"rekey_after_time=120-180","rekey_after_time=20-25")};if err=dev.IpcSet(cfg);err!=nil{panic(err)};if err=dev.Up();err!=nil{panic(err)}
 for _,ip:=range []string{"1.1.1.1","8.8.8.8"}{listener,listenErr:=stack.ListenTCP(&net.TCPAddr{IP:net.ParseIP(ip),Port:443});if listenErr!=nil{panic(listenErr)};go func(host string,l net.Listener){for{client,acceptErr:=l.Accept();if acceptErr!=nil{return};go func(){defer client.Close();remote,dialErr:=net.DialTimeout("tcp",net.JoinHostPort(host,"443"),10*time.Second);if dialErr!=nil{return};defer remote.Close();go io.Copy(remote,client);io.Copy(client,remote)}()}}(ip,listener)}
 handler:=http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){if r.URL.Path=="/load"{w.Header().Set("Content-Length","2097152");block:=bytes.Repeat([]byte{0x5a},32768);for i:=0;i<64;i++{if _,err:=w.Write(block);err!=nil{return}};return};fmt.Fprintln(w,"ASTER_TUNNEL_OK");fmt.Println("HTTP_THROUGH_TUNNEL "+r.RemoteAddr)})
 echo:=func(pc net.PacketConn){buffer:=make([]byte,4096);for{n,addr,err:=pc.ReadFrom(buffer);if err!=nil{return};pc.WriteTo(buffer[:n],addr)}}
 udp,err:=stack.ListenUDP(&net.UDPAddr{IP:net.ParseIP("10.8.0.1"),Port:17903});if err!=nil{panic(err)};go echo(udp)
 localUDP,err:=net.ListenPacket("udp4","127.0.0.1:17903");if err!=nil{panic(err)};go echo(localUDP)
 go http.ListenAndServe("127.0.0.1:17906",http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){io.WriteString(w,"DIRECT_OK")}))
 wg,err:=stack.ListenTCP(&net.TCPAddr{IP:net.ParseIP("10.8.0.1"),Port:17903});if err!=nil{panic(err)}
 go http.Serve(wg,handler);fmt.Println("HOST_READY");if err=http.ListenAndServe("127.0.0.1:17903",handler);err!=nil{panic(err)}
}
