// Local integration regression for parallel encrypted DNS and resolver resets.
// Run after the .NET tests: vendor/go/bin/go.exe run tests/dns_resilience.go
package main

import (
 "bytes"
 "encoding/binary"
 "encoding/json"
 "encoding/pem"
 "fmt"
 "io"
 "net"
 "net/http"
 "net/http/httptest"
 "os"
 "os/exec"
 "path/filepath"
 "strings"
 "sync"
 "sync/atomic"
 "time"
)

func check(err error) { if err != nil { panic(err) } }
func main() {
 root, err := os.Getwd(); check(err)
 raw, err := os.ReadFile(filepath.Join(root,"tests/generated/site-routing.json")); check(err)
 var cfg map[string]any; check(json.Unmarshal(raw,&cfg))
 var primarySlow atomic.Bool; primarySlow.Store(true)
 var primaryCount, backupCount, h2Count atomic.Int32
 makeServer := func(primary bool) *httptest.Server {
  srv := httptest.NewUnstartedServer(http.HandlerFunc(func(w http.ResponseWriter,r *http.Request) {
   if r.ProtoMajor == 2 { h2Count.Add(1) }
   if primary { primaryCount.Add(1) } else { backupCount.Add(1) }
   query, e := io.ReadAll(io.LimitReader(r.Body,4096)); if e != nil || len(query)<12 { return }
   delay := 300*time.Millisecond
   if primary { if primarySlow.Load() { delay=600*time.Millisecond } else { delay=10*time.Millisecond } }
   select { case <-time.After(delay): case <-r.Context().Done(): return }
   answer := append([]byte(nil),query...)
   answer[2]=0x81; answer[3]=0x80
   // A negative response must also win the race and stay NXDOMAIN.
   if bytes.Contains(query,[]byte("missing")) { answer[3]=0x83 } else {
    binary.BigEndian.PutUint16(answer[6:8],1)
    answer=append(answer,0xc0,0x0c,0,1,0,1,0,0,0,30,0,4,192,0,2,9)
   }
   w.Header().Set("Content-Type","application/dns-message"); w.Write(answer)
  }))
  srv.EnableHTTP2=true; srv.StartTLS(); return srv
 }
 primary:=makeServer(true); defer primary.Close()
 backup:=makeServer(false); defer backup.Close()
 dns:=cfg["dns"].(map[string]any)
 servers:=dns["servers"].([]any)
 for index,srv:=range []*httptest.Server{primary,backup} {
  server:=servers[index+1].(map[string]any)
  _,portText,e:=net.SplitHostPort(strings.TrimPrefix(srv.URL,"https://")); check(e)
  port,e:=net.LookupPort("tcp",portText); check(e)
  server["server"]="127.0.0.1"; server["server_port"]=port
  delete(server,"detour") // local fixtures use direct loopback sockets
  server["tls"]=map[string]any{"enabled":true,"server_name":"example.com","certificate":[]string{string(pem.EncodeToMemory(&pem.Block{Type:"CERTIFICATE",Bytes:srv.Certificate().Raw}))}}
 }
 dns["disable_cache"]=true
 for _,r:=range dns["rules"].([]any) {
  rule:=r.(map[string]any)
  if rule["action"]=="evaluate" { if rule["tag"]=="primary" { rule["timeout"]="150ms" } else { rule["timeout"]="2s" } }
 }
 free,err:=net.ListenPacket("udp","127.0.0.1:0"); check(err)
 port:=free.LocalAddr().(*net.UDPAddr).Port; free.Close()
 cfg["inbounds"]=[]any{map[string]any{"type":"direct","tag":"dns-fixture","listen":"127.0.0.1","listen_port":port,"network":"udp","override_address":"8.8.8.8","override_port":53}}
 cfg["outbounds"]=[]any{map[string]any{"type":"direct","tag":"proxy"},map[string]any{"type":"direct","tag":"direct"}}
 cfg["route"]=map[string]any{"default_domain_resolver":"bootstrap","rules":[]any{map[string]any{"inbound":[]string{"dns-fixture"},"action":"hijack-dns"}},"final":"proxy"}
 delete(cfg,"experimental")
 temp,err:=os.MkdirTemp(filepath.Join(root,"tests"),"dns-fixture-"); check(err); defer os.RemoveAll(temp)
 config:=filepath.Join(temp,"config.json"); data,err:=json.Marshal(cfg); check(err); check(os.WriteFile(config,data,0600))
 core:=filepath.Join(root,"vendor/sing-box-1.14.3-windows-amd64/sing-box.exe")
 if output,e:=exec.Command(core,"check","-c",config).CombinedOutput(); e!=nil {panic(string(output))}
 var logs bytes.Buffer
 cmd:=exec.Command(core,"run","-c",config); cmd.Stderr=&logs; cmd.Stdout=&logs
 check(cmd.Start()); defer func(){cmd.Process.Kill();cmd.Wait()}()
 time.Sleep(500*time.Millisecond)
 query:=func(name string,id int) (time.Duration,error) {
  start:=time.Now(); packet:=make([]byte,12); binary.BigEndian.PutUint16(packet[:2],uint16(id)); packet[2]=1; packet[5]=1
  for _,label:=range strings.Split(name,".") {packet=append(packet,byte(len(label)));packet=append(packet,[]byte(label)...)}
  packet=append(packet,0,0,1,0,1)
  conn,e:=net.Dial("udp",fmt.Sprintf("127.0.0.1:%d",port)); if e!=nil{return 0,e}; defer conn.Close()
  conn.SetDeadline(time.Now().Add(3*time.Second)); _,e=conn.Write(packet);if e!=nil{return 0,e}
  reply:=make([]byte,4096); n,e:=conn.Read(reply);if e!=nil{return 0,e}
  expected:=byte(0);if strings.Contains(name,"missing"){expected=3}
  if n<12 || binary.BigEndian.Uint16(reply[:2])!=uint16(id) || reply[3]&15!=expected{return 0,fmt.Errorf("invalid DNS response")}
  return time.Since(start),nil
 }
 // Let one timed-out primary HTTP/2 request reset its connection while many
 // other queries are outstanding. The independent backup must serve them all.
 var wg sync.WaitGroup; var failed atomic.Int32; var maxMillis atomic.Int64
 for i:=0;i<32;i++ {wg.Add(1);go func(index int){defer wg.Done();duration,e:=query(fmt.Sprintf("load%d.example",index),index+1);if e!=nil{failed.Add(1);return};for {old:=maxMillis.Load();if duration.Milliseconds()<=old||maxMillis.CompareAndSwap(old,duration.Milliseconds()){break}}}(i)}
 wg.Wait()
 fmt.Printf("DNS under primary timeout/reset: %d/32 OK; max=%d ms; primary=%d backup=%d HTTP2=%d\n",32-failed.Load(),maxMillis.Load(),primaryCount.Load(),backupCount.Load(),h2Count.Load())
 if failed.Load()!=0||maxMillis.Load()>2000||h2Count.Load()==0 {cmd.Process.Kill();cmd.Wait();fmt.Println(logs.String());panic("parallel DNS fallback regression")}
 _,err=query("missing.example",100);check(err);fmt.Println("NXDOMAIN is preserved")
 primarySlow.Store(false)
 duration,err:=query("recovered.example",101);check(err)
 fmt.Printf("Recovered primary: %d ms\n",duration.Milliseconds());if duration>200*time.Millisecond{panic("primary did not recover")}
 fmt.Println("DNS RESILIENCE PASS")
}
