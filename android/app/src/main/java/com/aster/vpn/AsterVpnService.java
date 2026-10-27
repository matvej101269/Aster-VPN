package com.aster.vpn;
import android.app.*;
import android.content.*;
import android.net.*;
import android.os.*;
import org.json.*;
import libv2ray.*;
import java.util.concurrent.*;


public final class AsterVpnService extends VpnService {
 public static final String START="com.aster.vpn.START",STOP="com.aster.vpn.STOP",LAST="com.aster.vpn.LAST",SWITCH="com.aster.vpn.SWITCH";
 public static volatile String state="Отключено",serverName="",profileId="";
 public static volatile boolean active=false,connecting=false;
 private final ScheduledExecutorService worker=Executors.newSingleThreadScheduledExecutor();
 private ScheduledFuture<?> trafficTask;private long trafficAt;private volatile String trafficText=TrafficRates.label(0,0);
 private ParcelFileDescriptor tun;private CoreController core;private AwgBridgeClient awg;
 private volatile boolean stopRequested;private volatile int latestStartId;private volatile boolean stopping;
 private static final int NOTIFICATION=1;
 public static void command(Context c,String action){Intent intent=new Intent(c,AsterVpnService.class).setAction(action);c.startForegroundService(intent);}
 @Override public void onCreate(){super.onCreate();getSystemService(NotificationManager.class).createNotificationChannel(new NotificationChannel("vpn","VPN-соединение",NotificationManager.IMPORTANCE_LOW));}
 private Notification notification(){
  PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,AsterVpnService.class).setAction(STOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  return new Notification.Builder(this,"vpn").setSmallIcon(com.aster.vpn.R.drawable.ic_tile).setContentTitle("Aster VPN"+(serverName.isEmpty()?"":" · "+serverName)).setContentText(active?trafficText:state).setStyle(new Notification.BigTextStyle().bigText(active?state+"\n"+trafficText:state)).setOnlyAlertOnce(true).setShowWhen(false).setCategory(Notification.CATEGORY_SERVICE).setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null,"Отключить",stop).build()).build();
 }
 private void report(String message){state=message;ConnectionLog.add(this,message);getSystemService(NotificationManager.class).notify(NOTIFICATION,notification());AsterTileService.refresh(this);sendBroadcast(new Intent("com.aster.vpn.STATE").setPackage(getPackageName()));}
 @Override public int onStartCommand(Intent intent,int flags,int startId){
  startForeground(NOTIFICATION,notification());String action=intent==null?LAST:intent.getAction();
  if(STOP.equals(action)){ConnectionLog.add(this,"Запрошено отключение VPN");latestStartId=startId;stopRequested=true;stopping=true;worker.execute(()->{close();finishService(startId);});return START_NOT_STICKY;}
  if(!stopping&&(connecting||active&&!SWITCH.equals(action)))return START_STICKY;
  latestStartId=startId;connecting=true;report("Подключение…");
  worker.execute(()->{if(latestStartId!=startId)return;stopRequested=false;stopping=false;try{if(SWITCH.equals(action))close();connecting=true;connect(!START.equals(action)&&!SWITCH.equals(action));}catch(Exception e){close();state="Ошибка: "+safeMessage(e);finishService(startId);}finally{connecting=false;AsterTileService.refresh(this);}});
  return START_STICKY;
 }
 private void connect(boolean useLast)throws Exception{
  if(VpnService.prepare(this)!=null)throw new IllegalStateException("Нужно разрешение Android на VPN");
  Store store=new Store(this);JSONObject prefs=store.read(),profile=Store.connection(prefs,useLast);if(profile==null)throw new IllegalStateException("Сначала выберите сервер в Aster");
  ConnectionLog.add(this,"Запуск профиля: "+profile.optString("protocol"));serverName=profile.getString("name");profileId=profile.getString("id");Builder builder=new Builder().setSession("Aster · "+serverName).setMtu(XraySettings.MTU).setBlocking(false);
  if(Build.VERSION.SDK_INT>=29)builder.setMetered(false);
  applyApps(builder,prefs);for(String route:LanRoutes.tunnel(prefs.optBoolean("bypassLan",true))){String[] cidr=route.split("/");builder.addRoute(cidr[0],Integer.parseInt(cidr[1]));}
  boolean isAwg=profile.getString("protocol").equals("amneziawg");JSONObject override=null;
  builder.addAddress("172.29.0.1",30);builder.addDnsServer("1.1.1.1");builder.addDnsServer("8.8.8.8");
  if(isAwg){
   AwgConfig config=AwgConfig.parse(profile.getString("config"));JSONArray addresses=new JSONArray(),dns=new JSONArray();boolean ipv4=false;
   for(String cidr:config.addresses){String address=cidr.split("/")[0];addresses.put(address);if(!address.contains(":"))ipv4=true;}if(!ipv4)throw new IllegalArgumentException("AWG требует IPv4 адрес");for(String address:config.dns)dns.put(address);
   String password=java.util.UUID.randomUUID().toString()+java.util.UUID.randomUUID();
   awg=new AwgBridgeClient(this,new JSONObject().put("addresses",addresses).put("dns",dns).put("mtu",Math.min(config.mtu,XraySettings.MTU)).put("uapi",config.uapi).put("debug",BuildConfig.DEBUG).put("password",password).toString(),()->{if(!worker.isShutdown())worker.execute(()->{close();state="Ошибка: процесс AWG остановился";finishService();});});int port=awg.start();
   if(port<0)throw new IllegalStateException("Не удалось запустить AWG ("+port+")");
   override=XraySettings.awgOutbound(port,password);
  }
  // Android blocks IPv6 for captured applications; all routing uses one TUN engine.
  if(stopRequested){close();return;}tun=builder.establish();if(tun==null)throw new IllegalStateException("Android не разрешил создать VPN");
  if(prefs.optInt("geoMode")!=0)GeoAssets.ensure(this);
  CoreRuntime.configure();
  go.Seq.setContext(getApplicationContext());
  Libv2ray.initCoreEnv(getFilesDir().getAbsolutePath(),Identity.xudpKey(Identity.hwid(this)));
  core=Libv2ray.newCoreController(new CoreCallbackHandler(){public long startup(){return 0;}public long shutdown(){return 0;}public long onEmitStatus(long code,String message){ConnectionLog.add(AsterVpnService.this,"Событие ядра Xray: "+code);return 0;}});
  core.startLoop(XraySettings.build(profile,prefs,override),tun.getFd());
  if(!core.getIsRunning())throw new IllegalStateException("Xray не запустился");
  if(stopRequested){close();return;}active=true;connecting=false;store.change(data->data.put("lastConnection",profile));report("VPN включён");trafficAt=SystemClock.elapsedRealtime();trafficText=TrafficRates.label(0,0);trafficTask=worker.scheduleWithFixedDelay(this::sampleTraffic,2,2,TimeUnit.SECONDS);
 }
 private void applyApps(Builder b,JSONObject data)throws Exception{
  int mode=data.optInt("mode");JSONArray apps=data.optJSONArray("apps");
  if(mode==1){int count=0;if(apps!=null)for(int i=0;i<apps.length();i++){String pkg=apps.getString(i);if(pkg.equals(getPackageName()))continue;try{b.addAllowedApplication(pkg);count++;}catch(android.content.pm.PackageManager.NameNotFoundException ignored){}}if(count==0)throw new IllegalStateException("Выберите приложения для VPN или режим «Все приложения»");}
  else{b.addDisallowedApplication(getPackageName());if(mode==2&&apps!=null)for(int i=0;i<apps.length();i++)try{b.addDisallowedApplication(apps.getString(i));}catch(android.content.pm.PackageManager.NameNotFoundException ignored){}}
 }
 private void sampleTraffic(){if(!active||core==null||!core.getIsRunning())return;try{long now=SystemClock.elapsedRealtime();long[] bytes=TrafficRates.bytes(core.queryAllOutboundTrafficStats());String next=TrafficRates.label(TrafficRates.perSecond(bytes[0],now-trafficAt),TrafficRates.perSecond(bytes[1],now-trafficAt));trafficAt=now;if(!next.equals(trafficText)){trafficText=next;getSystemService(NotificationManager.class).notify(NOTIFICATION,notification());}}catch(Exception ignored){}}
 private void close(){if(trafficTask!=null){trafficTask.cancel(false);trafficTask=null;}trafficText=TrafficRates.label(0,0);active=false;connecting=false;if(core!=null){try{core.stopLoop();}catch(Exception ignored){}core=null;}if(awg!=null){awg.close();awg=null;}if(tun!=null){try{tun.close();}catch(Exception ignored){}tun=null;}}
 private void finishService(){finishService(latestStartId);}
 private void finishService(int expected){new Handler(Looper.getMainLooper()).post(()->{if(expected!=latestStartId)return;if(stopRequested)state="Отключено";ConnectionLog.add(this,state);stopForeground(STOP_FOREGROUND_REMOVE);AsterTileService.refresh(this);sendBroadcast(new Intent("com.aster.vpn.STATE").setPackage(getPackageName()));stopSelf();});}
 @Override public void onRevoke(){ConnectionLog.add(this,"Android отозвал разрешение VPN");stopRequested=true;worker.execute(()->{close();finishService();});super.onRevoke();}
 @Override public void onDestroy(){stopRequested=true;worker.execute(()->{close();if(!state.startsWith("Ошибка:"))state="Отключено";AsterTileService.refresh(this);});worker.shutdown();super.onDestroy();}
 static String safeMessage(Exception e){if(e instanceof IllegalStateException||e instanceof IllegalArgumentException)return e.getMessage()==null?"Неверная конфигурация":e.getMessage();String detail=String.valueOf(e.getMessage()).toLowerCase(java.util.Locale.ROOT);if(detail.contains("xudp")||detail.contains("base key"))return "Ошибка ключа инициализации Xray (XUDP)";if(detail.contains("config")||detail.contains("failed to build"))return "Xray отклонил настройки сервера. Обновите подписку.";if(detail.contains("certificate")||detail.contains("tls")||detail.contains("reality"))return "Не удалось установить защищённое соединение с сервером";if(detail.contains("permission")||detail.contains("bad file descriptor"))return "Android отклонил сетевой интерфейс VPN";if(e instanceof java.net.UnknownHostException)return "Не удалось найти адрес сервера. Проверьте сеть.";if(e instanceof java.net.SocketTimeoutException)return "Сервер не ответил вовремя";return "Не удалось запустить VPN ("+e.getClass().getSimpleName()+"). Обновите подписку и повторите подключение.";}
}
