package netstack
import("net";"net/netip";"os";"runtime";"sync";"testing";"time";"gvisor.dev/gvisor/pkg/refs")
func TestMain(m *testing.M){refs.SetLeakMode(refs.LeaksPanic);code:=m.Run();runtime.GC();runtime.GC();time.Sleep(100*time.Millisecond);os.Exit(code)}
func TestPacketOwnershipAndShutdown(t *testing.T){
 a,na,err:=CreateNetTUN([]netip.Addr{netip.MustParseAddr("10.99.0.1")},nil,1280);if err!=nil{t.Fatal(err)}
 b,nb,err:=CreateNetTUN([]netip.Addr{netip.MustParseAddr("10.99.0.2")},nil,1280);if err!=nil{t.Fatal(err)}
 var workers sync.WaitGroup;workers.Add(2)
 pump:=func(read interface{Read([][]byte,[]int,int)(int,error)},write interface{Write([][]byte,int)(int,error)}){defer workers.Done();buf:=[][]byte{make([]byte,1500)};sizes:=make([]int,1);for{n,e:=read.Read(buf,sizes,0);if e!=nil{return};if n>0{if _,e=write.Write([][]byte{buf[0][:sizes[0]]},0);e!=nil{return}}}}
 go pump(a,b);go pump(b,a)
 server,err:=nb.ListenUDP(&net.UDPAddr{IP:net.ParseIP("10.99.0.2"),Port:19001});if err!=nil{t.Fatal(err)}
 echoDone:=make(chan struct{});go func(){defer close(echoDone);buf:=make([]byte,1500);for{n,addr,e:=server.ReadFrom(buf);if e!=nil{return};server.WriteTo(buf[:n],addr)}}()
 client,err:=na.DialUDP(nil,&net.UDPAddr{IP:net.ParseIP("10.99.0.2"),Port:19001});if err!=nil{t.Fatal(err)}
 for i:=0;i<3000;i++{client.SetDeadline(time.Now().Add(time.Second));message:=[]byte{1,2,3,byte(i)};if _,err=client.Write(message);err!=nil{t.Fatal(err)};buf:=make([]byte,16);n,e:=client.Read(buf);if e!=nil||n!=4||buf[3]!=byte(i){t.Fatalf("datagram %d: n=%d err=%v",i,n,e)}}
 client.Close();server.Close();<-echoDone;a.Close();b.Close();a.Close();done:=make(chan struct{});go func(){workers.Wait();close(done)}();select{case <-done:case <-time.After(time.Second):t.Fatal("shutdown left TUN workers blocked")}
}
