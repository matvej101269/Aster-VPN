package com.aster.vpn;
import android.app.*;
import android.content.*;
import android.os.*;
import org.json.*;
import java.util.*;

/** Emulator-only integration runner; never included in the release APK. */
public final class NativeChecks extends Instrumentation {
 private Bundle args;
 @Override public void onCreate(Bundle args){super.onCreate(args);this.args=args;start();}
 @Override public void onStart(){Bundle result=new Bundle();try{
  Context c=getTargetContext();String mode=args.getString("mode","native");
  if(mode.equals("qr")){runQrChecks(c);result.putString("result","PASS: QR button opens scanner; camera preview resumes; scanner closes and reopens without crash");}else if(mode.equals("ui")){runUiChecks(c);result.putString("result","PASS: all 65 rows rendered; settings persisted; connection log dialog contains event history");}else if(mode.equals("native")){
   if("geo".equals(args.getString("rules")))GeoAssets.ensure(c);go.Seq.setContext(c);libv2ray.Libv2ray.initCoreEnv(c.getFilesDir().getAbsolutePath(),Identity.xudpKey(Identity.hwid(c)));
   libv2ray.CoreController core=libv2ray.Libv2ray.newCoreController(new libv2ray.CoreCallbackHandler(){public long startup(){return 0;}public long shutdown(){return 0;}public long onEmitStatus(long n,String s){return 0;}});
   ParcelFileDescriptor[] pair=ParcelFileDescriptor.createSocketPair();try{
    JSONObject profile=Profiles.parse("vless://f1f1408b-959e-4c09-bb5b-981c8ba33101@10.0.2.2:17901?type=raw&security=none#Local%20VLESS").getJSONObject(0);
    libv2ray.Libv2ray.initCoreEnv(c.getFilesDir().getAbsolutePath(),"00000000-0000-0000-0000-000000000000");boolean rejected=false;
    try{core.startLoop(XraySettings.build(profile,new JSONObject()),pair[0].getFd());}catch(Exception expected){rejected=true;}
    if(!rejected)throw new AssertionError("Old UUID key should be rejected");
    libv2ray.Libv2ray.initCoreEnv(c.getFilesDir().getAbsolutePath(),Identity.xudpKey(Identity.hwid(c)));
    core.startLoop(XraySettings.build(profile,"geo".equals(args.getString("rules"))?new JSONObject().put("geoMode",1).put("countriesVpn",new JSONArray().put("ru")):new JSONObject()),pair[0].getFd());if(!core.getIsRunning())throw new AssertionError("Xray is not running");core.stopLoop();
   }finally{pair[0].close();pair[1].close();}
   result.putString("result","PASS: old XUDP UUID rejected; 32-byte key starts real Android Xray TUN");
  }else{
   JSONArray profiles=new JSONArray();profiles.put(Profiles.parse("vless://f1f1408b-959e-4c09-bb5b-981c8ba33101@10.0.2.2:17901?type=raw&security=none#%F0%9F%87%B3%F0%9F%87%B1%20Netherlands%20%7C%20Amsterdam").getJSONObject(0));
   String key=Base64.getEncoder().encodeToString(new byte[]{1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32});
   String awg="[Interface]\nPrivateKey="+key+"\nAddress=10.8.0.2/32\nDNS=1.1.1.1\nJc=4\nJmin=40\nJmax=70\nS1=16\nS2=16\nS3=16\nS4=16\nH1=1\nH2=2\nH3=3\nH4=4\nHeaderProtectionKey="+key+"\nContentPaddingAddition=16-64\nRekeyAfterTime=120-180\nRekeyTimeout=4-8\nRejectAfterTime=200-300\nKeepaliveTimeout=10-20\nMaxHandshakeAttempts=10-20\nRandomTrailers=true\nDisableCookies=false\n[Peer]\nPublicKey=DXmWAPb/ruLhIea496Bdxmh0tR2zEC0NcfeZoJy0xGE=\nEndpoint=10.0.2.2:17902\nAllowedIPs=0.0.0.0/0\nPersistentKeepalive=25";
   if("true".equals(args.getString("fast")))awg=awg.replace("RekeyAfterTime=120-180","RekeyAfterTime=20-25");
   JSONObject awgProfile=new JSONObject().put("id","demo-awg").put("name","🇩🇪 Germany | Frankfurt").put("protocol","amneziawg").put("config",awg);profiles.put(awgProfile);
   profiles.put(Profiles.parse("vless://f1f1408b-959e-4c09-bb5b-981c8ba33101@10.0.2.2:17901?type=raw&security=none#Finland%20%7C%20Helsinki").getJSONObject(0));
   JSONObject sub=new JSONObject().put("id","demo").put("name","Aster · Personal").put("url","").put("servers",profiles).put("updated",System.currentTimeMillis()).put("usage","upload=1073741824; download=23622320128; total=214748364800");
   new Store(c).change(d->{d.put("mode",0);d.put("siteMode",0);d.put("geoMode",0);d.put("bypassLan",false);d.put("subscriptions",new JSONArray().put(sub));d.put("selected",mode.equals("awg")?"demo-awg":profiles.getJSONObject(0).getString("id"));});
   startActivitySync(new Intent(c,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
   if(mode.equals("xhttp")||mode.equals("cycle")){JSONObject x=Profiles.parse("vless://f1f1408b-959e-4c09-bb5b-981c8ba33101@10.0.2.2:17904?type=xhttp&security=none&mode=packet-up&path=%2Faster-test#Local%20XHTTP").getJSONObject(0);new Store(c).change(d->{d.getJSONArray("subscriptions").getJSONObject(0).getJSONArray("servers").put(x);d.put("selected",x.getString("id"));});}
   if("sites".equals(args.getString("rules")))new Store(c).change(d->{d.put("siteMode",1);d.put("sitesVpn",new JSONArray().put("aster-test.invalid"));});
   if("geo".equals(args.getString("rules")))new Store(c).change(d->{d.put("geoMode",1);d.put("countriesVpn",new JSONArray().put("ru"));});
   if(mode.equals("cycle")){JSONArray saved=new Store(c).read().getJSONArray("subscriptions").getJSONObject(0).getJSONArray("servers");int[] order={0,1,3,1,0,1,3,1};for(int index:order){JSONObject profile=saved.getJSONObject(index);new Store(c).change(d->d.put("selected",profile.getString("id")));AsterVpnService.command(c,AsterVpnService.active?AsterVpnService.SWITCH:AsterVpnService.START);long until=System.currentTimeMillis()+20000;while(System.currentTimeMillis()<until&&(!AsterVpnService.active||AsterVpnService.connecting||!AsterVpnService.profileId.equals(profile.getString("id"))))Thread.sleep(50);if(!AsterVpnService.active||!AsterVpnService.profileId.equals(profile.getString("id")))throw new AssertionError("Switch failed: "+AsterVpnService.state);verifyHttp(c,index==1?"http://10.8.0.1:17903/":"http://198.18.0.1:17903/");}AsterVpnService.command(c,AsterVpnService.STOP);Thread.sleep(600);result.putString("result","PASS: eight VLESS / AWG / XHTTP switches with HTTP in the same app process");}
   else if(mode.equals("vless")||mode.equals("awg")||mode.equals("xhttp")){AsterVpnService.command(c,AsterVpnService.START);long deadline=System.currentTimeMillis()+15000;while(System.currentTimeMillis()<deadline&&!AsterVpnService.active){Thread.sleep(100);if(AsterVpnService.state.startsWith("Ошибка:"))throw new AssertionError(AsterVpnService.state);}if(!AsterVpnService.active)throw new AssertionError("VPN startup timeout");java.util.concurrent.CountDownLatch latch=new java.util.concurrent.CountDownLatch(1);String[] response={"No response",""};
   BroadcastReceiver receiver=new BroadcastReceiver(){public void onReceive(Context ctx,Intent intent){response[0]=intent.getStringExtra("result");response[1]=intent.getStringExtra("stats");latch.countDown();}};
   if(Build.VERSION.SDK_INT>=33)c.registerReceiver(receiver,new IntentFilter("com.aster.vpn.PROBE_RESULT"),Context.RECEIVER_EXPORTED);else c.registerReceiver(receiver,new IntentFilter("com.aster.vpn.PROBE_RESULT"));
   try{c.startActivity(new Intent().setClassName("com.aster.probe","com.aster.probe.ProbeActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("dns","true".equals(args.getString("dns"))).putExtra("duration",Integer.parseInt(args.getString("duration","0"))).putExtra("load","true".equals(args.getString("load"))).putExtra("url",args.containsKey("url")?args.getString("url"):mode.equals("awg")?"http://10.8.0.1:17903/":"http://198.18.0.1:17903/"));if(!latch.await(Integer.parseInt(args.getString("duration","0"))+180,java.util.concurrent.TimeUnit.SECONDS)||!"ASTER_TUNNEL_OK".equals(response[0].trim()))throw new AssertionError("Tunnel HTTP failed: "+response[0]);}finally{c.unregisterReceiver(receiver);}
   result.putString("result","PASS: real Android VPN + HTTP through "+mode+" "+response[1]);sendStatus(0,result);if("true".equals(args.getString("capture")))Thread.sleep(30000);AsterVpnService.command(c,AsterVpnService.STOP);Thread.sleep(500);}else result.putString("result","Demo profiles ready");
  }
  finish(Activity.RESULT_OK,result);
 }catch(Throwable e){result.putString("failure",e.toString());finish(Activity.RESULT_CANCELED,result);}}
 private void verifyHttp(Context c,String url)throws Exception{java.util.concurrent.CountDownLatch latch=new java.util.concurrent.CountDownLatch(1);String[] response={"none"};BroadcastReceiver receiver=new BroadcastReceiver(){public void onReceive(Context ctx,Intent intent){response[0]=intent.getStringExtra("result");latch.countDown();}};c.registerReceiver(receiver,new IntentFilter("com.aster.vpn.PROBE_RESULT"),Context.RECEIVER_EXPORTED);try{c.startActivity(new Intent().setClassName("com.aster.probe","com.aster.probe.ProbeActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("url",url));if(!latch.await(30,java.util.concurrent.TimeUnit.SECONDS)||!response[0].trim().equals("ASTER_TUNNEL_OK"))throw new AssertionError("After switch: "+response[0]);}finally{c.unregisterReceiver(receiver);}}


 private Object field(Object target,String name)throws Exception{java.lang.reflect.Field f=target.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(target);}
 private void runQrChecks(Context c)throws Exception{
  c.getSharedPreferences("app_updates",0).edit().putLong("checked",System.currentTimeMillis()).commit();
  new Store(c).change(d->{d.put("updateOnOpen",false);d.put("pingOnOpen",false);d.put("connectOnOpen",false);});
  Activity main=startActivitySync(new Intent(c,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
  for(int i=0;i<100;i++){final boolean[] ready={false};runOnMainSync(()->{try{ready[0]=(boolean)field(main,"ready");}catch(Exception e){throw new RuntimeException(e);}});if(ready[0])break;Thread.sleep(100);}
  for(int cycle=0;cycle<2;cycle++){
   ActivityMonitor monitor=addMonitor(QrScanActivity.class.getName(),null,false);
   runOnMainSync(()->{android.view.View button=findText(main.getWindow().getDecorView(),"QR-код");if(button==null||!button.performClick())throw new AssertionError("QR button missing or unresponsive");});
   Activity scanner=waitForMonitorWithTimeout(monitor,5000);removeMonitor(monitor);if(scanner==null)throw new AssertionError("Scanner did not open");
   Thread.sleep(1500);waitForIdleSync();
   runOnMainSync(()->{if(scanner.isFinishing()||scanner.isDestroyed())throw new AssertionError("Scanner closed unexpectedly");com.journeyapps.barcodescanner.DecoratedBarcodeView view=scanner.findViewById(com.google.zxing.client.android.R.id.zxing_barcode_scanner);if(view==null||!view.getBarcodeView().isPreviewActive())throw new AssertionError("Camera preview not active");scanner.finish();});
   Thread.sleep(500);waitForIdleSync();
  }
  runOnMainSync(main::finish);
 }
 private void call(Object target,String name,Class<?>[] types,Object...values)throws Exception{java.lang.reflect.Method method=target.getClass().getDeclaredMethod(name,types);method.setAccessible(true);method.invoke(target,values);}
 private android.view.View findText(android.view.View view,String text){if(view instanceof android.widget.TextView&&text.contentEquals(((android.widget.TextView)view).getText()))return view;if(view instanceof android.view.ViewGroup){android.view.ViewGroup group=(android.view.ViewGroup)view;for(int i=0;i<group.getChildCount();i++){android.view.View found=findText(group.getChildAt(i),text);if(found!=null)return found;}}return null;}
 private void runUiChecks(Context c)throws Exception{
  JSONArray profiles=new JSONArray();for(int i=0;i<65;i++)profiles.put(new JSONObject().put("id","ui-"+i).put("name","Test server "+i).put("protocol","vless").put("outbound",new JSONObject().put("protocol","vless").put("settings",new JSONObject().put("vnext",new JSONArray().put(new JSONObject().put("address","127.0.0.1").put("port",19000+i))))));
  new Store(c).change(d->{d.put("subscriptions",new JSONArray().put(new JSONObject().put("id","ui").put("name","UI checks").put("url","").put("servers",profiles)));d.put("selected","ui-0");d.put("updateOnOpen",false);d.put("pingOnOpen",false);d.put("connectOnOpen",false);});
  c.getSharedPreferences("com.aster.vpn.MainActivity",0).edit().clear().commit();
  Activity activity=startActivitySync(new Intent(c,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
  for(int i=0;i<100;i++){final boolean[] ready={false};runOnMainSync(()->{try{ready[0]=(boolean)field(activity,"ready");}catch(Exception e){throw new RuntimeException(e);}});if(ready[0])break;Thread.sleep(100);}
  runOnMainSync(()->{try{if(((java.util.List<?>)field(activity,"serverRows")).size()!=65)throw new AssertionError("Expected all 65 server rows");call(activity,"navigate",new Class<?>[]{String.class},"Настройки");android.view.View root=activity.getWindow().getDecorView();for(String label:new String[]{"Обновлять при открытии","Пинговать при открытии","Подключаться при открытии"}){android.view.View control=findText(root,label);if(!(control instanceof android.widget.Switch))throw new AssertionError("Missing switch: "+label);((android.widget.Switch)control).setChecked(true);}}catch(Exception e){throw new RuntimeException(e);}});
  for(int i=0;i<100;i++){JSONObject saved=new Store(c).read();if(saved.optBoolean("updateOnOpen")&&saved.optBoolean("pingOnOpen")&&saved.optBoolean("connectOnOpen"))break;Thread.sleep(50);}
  JSONObject saved=new Store(c).read();if(!saved.optBoolean("updateOnOpen")||!saved.optBoolean("pingOnOpen")||!saved.optBoolean("connectOnOpen"))throw new AssertionError("Opening preferences were not persisted");
  ConnectionLog.add(c,"UI event history check");
  runOnMainSync(()->{android.view.View label=findText(activity.getWindow().getDecorView(),"Журнал подключения");if(label==null)throw new AssertionError("Missing log row");android.view.View clickable=label;while(!clickable.isClickable()&&clickable.getParent() instanceof android.view.View)clickable=(android.view.View)clickable.getParent();if(!clickable.performClick())throw new AssertionError("Log row does not respond");});
  waitForIdleSync();android.view.accessibility.AccessibilityNodeInfo root=getUiAutomation().getRootInActiveWindow();if(root==null||root.findAccessibilityNodeInfosByText("UI event history check").isEmpty())throw new AssertionError("Log dialog did not show saved history");
  runOnMainSync(activity::finish);
 }
}


