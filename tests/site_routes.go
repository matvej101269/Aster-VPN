// Exercise domain routing in the real core against two local marker proxies.
package main

import (
 "bytes"
 "encoding/json"
 "fmt"
 "io"
 "net"
 "net/http"
 "net/http/httptest"
 "net/url"
 "os"
 "os/exec"
 "path/filepath"
 "time"
)

func main() {
 must:=func(err error){if err!=nil{panic(err)}}
 root,err:=os.Getwd();must(err)
 fixture:="site-routing.json";mode:="legacy"
 if len(os.Args)>1 {mode=os.Args[1];fixture="routing-mode-"+mode+".json"}
 raw,err:=os.ReadFile(filepath.Join(root,"tests/generated",fixture));must(err)
 var cfg map[string]any;must(json.Unmarshal(raw,&cfg))
 marker:=func(name string)*httptest.Server {
  return httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){
   conn,rw,e:=w.(http.Hijacker).Hijack();if e!=nil{return};defer conn.Close()
   conn.SetDeadline(time.Now().Add(3*time.Second))
   fmt.Fprint(rw,"HTTP/1.1 200 Connection established\r\n\r\n");rw.Flush()
   if _,e=http.ReadRequest(rw.Reader);e!=nil{return}
   fmt.Fprintf(rw,"HTTP/1.1 200 OK\r\nContent-Length: %d\r\nConnection: close\r\n\r\n%s",len(name),name);rw.Flush()
  }))
 }
 direct:=marker("direct");defer direct.Close();proxy:=marker("proxy");defer proxy.Close()
 outbounds:=[]any{}
 for tag,srv:=range map[string]*httptest.Server{"direct":direct,"proxy":proxy}{
  port:=srv.Listener.Addr().(*net.TCPAddr).Port
  outbounds=append(outbounds,map[string]any{"type":"http","tag":tag,"server":"127.0.0.1","server_port":port})
 }
 listener,err:=net.Listen("tcp","127.0.0.1:0");must(err);port:=listener.Addr().(*net.TCPAddr).Port;listener.Close()
 cfg["inbounds"]=[]any{map[string]any{"type":"mixed","tag":"web-test","listen":"127.0.0.1","listen_port":port}}
 cfg["outbounds"]=outbounds;delete(cfg,"experimental")
 route:=cfg["route"].(map[string]any);delete(route,"rule_set")
 rules:=[]any{}
 for _,value:=range route["rules"].([]any){r:=value.(map[string]any);if _,ok:=r["rule_set"];!ok{rules=append(rules,r)}}
 route["rules"]=rules
 temp,err:=os.MkdirTemp(filepath.Join(root,"tests"),"site-fixture-");must(err);defer os.RemoveAll(temp)
 config:=filepath.Join(temp,"config.json");raw,err=json.Marshal(cfg);must(err);must(os.WriteFile(config,raw,0600))
 core:=filepath.Join(root,"vendor/sing-box-1.14.3-windows-amd64/sing-box.exe")
 if output,e:=exec.Command(core,"check","-c",config).CombinedOutput();e!=nil{panic(string(output))}
 var logs bytes.Buffer
 cmd:=exec.Command(core,"run","-c",config);cmd.Stderr=&logs;cmd.Stdout=&logs;must(cmd.Start());defer func(){cmd.Process.Kill();cmd.Wait()}()
 time.Sleep(400*time.Millisecond)
 address,_:=url.Parse(fmt.Sprintf("http://127.0.0.1:%d",port))
 client:=&http.Client{Transport:&http.Transport{Proxy:http.ProxyURL(address)},Timeout:3*time.Second}
 cases:=[]struct{domain,expected string}{
  {"example.com","direct"},{"www.example.com","direct"},{"video.example.com","proxy"},{"cdn.video.example.com","proxy"},
  {"youtube.com","proxy"},{"www.youtube.com","proxy"},{"notyoutube.com","direct"},{"video.example.com.evil.test","direct"},{"other.test","direct"},
 }
 if mode!="legacy" {
  cases=[]struct{domain,expected string}{{"example.com","proxy"},{"www.example.com","proxy"},{"youtube.com","proxy"},{"www.youtube.com","proxy"},{"notyoutube.com","proxy"},{"other.test","proxy"}}
  for i:=range cases {
   if mode=="1"&&cases[i].domain!="youtube.com"&&cases[i].domain!="www.youtube.com" {cases[i].expected="direct"}
   if mode=="2"&&(cases[i].domain=="example.com"||cases[i].domain=="www.example.com") {cases[i].expected="direct"}
  }
 }
 for _,test:=range cases{
  response,e:=client.Get("http://"+test.domain+"/test");must(e);body,e:=io.ReadAll(response.Body);response.Body.Close();must(e)
  if string(body)!=test.expected{panic(fmt.Sprintf("%s: got %s, want %s",test.domain,body,test.expected))}
  fmt.Printf("PASS %s -> %s\n",test.domain,test.expected)
 }
 fmt.Printf("SITE ROUTING PASS: %s, %d real-core routes; no external traffic\n",mode,len(cases))
}
