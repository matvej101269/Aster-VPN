package com.aster.vpn;

import android.app.*;
import android.content.*;
import android.content.pm.*;
import android.net.Uri;
import android.provider.Settings;
import android.widget.Toast;
import org.json.*;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.concurrent.Executors;
import java.util.concurrent.ExecutorService;
import java.util.regex.*;

final class AppUpdater {
 private static final ExecutorService IO=Executors.newSingleThreadExecutor();
 private static boolean busy;
 private static final String BASE="https://github.com/matvej101269/Aster-VPN/releases/download/";
 static final class Update {String version,url,digest;long size;}
 static int compare(String a,String b){String[] x=a.split("\\."),y=b.split("\\.");for(int i=0;i<Math.max(x.length,y.length);i++){int c=Integer.compare(i<x.length?Integer.parseInt(x[i]):0,i<y.length?Integer.parseInt(y[i]):0);if(c!=0)return c;}return 0;}
 private static HttpURLConnection connect(String url)throws Exception {
  HttpURLConnection c=(HttpURLConnection)new URL(url).openConnection();c.setConnectTimeout(15000);c.setReadTimeout(30000);c.setRequestProperty("User-Agent","AsterVPN/"+BuildConfig.VERSION_NAME);
  if(c.getResponseCode()!=200){int status=c.getResponseCode();c.disconnect();throw new IOException("HTTP "+status);}return c;
 }
 static Update select(JSONArray releases)throws Exception {
  Update best=null;Pattern pattern=Pattern.compile("^AsterVPN-(\\d+\\.\\d+\\.\\d+)-android\\.apk$");
  for(int i=0;i<releases.length();i++){JSONObject r=releases.getJSONObject(i);if(r.optBoolean("draft")||r.optBoolean("prerelease"))continue;JSONArray assets=r.getJSONArray("assets");
   for(int j=0;j<assets.length();j++){JSONObject a=assets.getJSONObject(j);Matcher m=pattern.matcher(a.optString("name"));if(!m.matches()||compare(m.group(1),BuildConfig.VERSION_NAME)<=0)continue;
    String url=a.optString("browser_download_url"),digest=a.optString("digest");long size=a.optLong("size");if(!url.startsWith(BASE)||!digest.matches("sha256:[a-f0-9]{64}")||size<=0||size>1073741824L)continue;
    if(best==null||compare(m.group(1),best.version)>0){best=new Update();best.version=m.group(1);best.url=url;best.digest=digest.substring(7);best.size=size;}
   }
  }return best;
 }
 static void check(Activity activity,boolean manual){
  check(activity,manual,false);
 }
 static void checkAtStartup(Activity activity){check(activity,false,true);}
 static boolean shouldCheck(boolean manual,boolean startup,long now,long checked){return manual||startup||now-checked>=21600000;}
 private static void check(Activity activity,boolean manual,boolean startup){
  if(busy)return;android.content.SharedPreferences prefs=activity.getSharedPreferences("app_updates",0);long now=System.currentTimeMillis();if(!shouldCheck(manual,startup,now,prefs.getLong("checked",0)))return;prefs.edit().putLong("checked",now).apply();busy=true;
  IO.execute(()->{try{HttpURLConnection c=connect("https://api.github.com/repos/matvej101269/Aster-VPN/releases?per_page=100");String body;try(InputStream in=c.getInputStream()){body=Subscriptions.read(in);}finally{c.disconnect();}Update update=select(new JSONArray(body));activity.runOnUiThread(()->{busy=false;if(activity.isDestroyed()||activity.isFinishing())return;if(update==null){if(manual)Toast.makeText(activity,"Новых обновлений нет",Toast.LENGTH_LONG).show();return;}if(activity instanceof MainActivity)((MainActivity)activity).offerAppUpdate(update);else showOffer(activity,update);});}
   catch(Exception e){activity.runOnUiThread(()->{busy=false;if(manual&&!activity.isDestroyed())Toast.makeText(activity,"Не удалось проверить обновление: "+e.getMessage(),Toast.LENGTH_LONG).show();});}});
 }
 static void showOffer(Activity activity,Update update){new AlertDialog.Builder(activity).setTitle("Доступно обновление Aster VPN").setMessage("Доступна версия "+update.version+". Установлена версия "+BuildConfig.VERSION_NAME+". Обновить сейчас?").setNegativeButton("Позже",null).setPositiveButton("Обновить",(d,w)->download(activity,update)).show();}
 private static void download(Activity activity,Update update){if(busy)return;busy=true;Toast.makeText(activity,"Загрузка обновления…",Toast.LENGTH_LONG).show();IO.execute(()->{
  File target=new File(activity.getCacheDir(),"update.apk");File partial=new File(activity.getCacheDir(),"update.part");
  try {HttpURLConnection c=connect(update.url);MessageDigest hash=MessageDigest.getInstance("SHA-256");long total=0;
   try(InputStream in=c.getInputStream();OutputStream out=new FileOutputStream(partial)){byte[] buffer=new byte[65536];int n;while((n=in.read(buffer))!=-1){total+=n;if(total>update.size)throw new IOException("Неверный размер APK");hash.update(buffer,0,n);out.write(buffer,0,n);}}finally{c.disconnect();}
   StringBuilder hex=new StringBuilder();for(byte b:hash.digest())hex.append(String.format(java.util.Locale.ROOT,"%02x",b&255));if(total!=update.size||!hex.toString().equals(update.digest))throw new IOException("SHA-256 APK не совпадает");
   PackageManager pm=activity.getPackageManager();int flags=android.os.Build.VERSION.SDK_INT>=28?PackageManager.GET_SIGNING_CERTIFICATES:PackageManager.GET_SIGNATURES;PackageInfo apk=pm.getPackageArchiveInfo(partial.getPath(),flags),installed=pm.getPackageInfo(activity.getPackageName(),flags);
   if(apk==null||!activity.getPackageName().equals(apk.packageName)||compare(apk.versionName,BuildConfig.VERSION_NAME)<=0||!update.version.equals(apk.versionName)||apk.versionCode<=BuildConfig.VERSION_CODE)throw new IOException("Неверный пакет или версия APK");
   Signature[] a=android.os.Build.VERSION.SDK_INT>=28?apk.signingInfo.getApkContentsSigners():apk.signatures,b=android.os.Build.VERSION.SDK_INT>=28?installed.signingInfo.getApkContentsSigners():installed.signatures;
   if(a==null||b==null||a.length==0||!Arrays.equals(a,b))throw new IOException("Подпись APK не совпадает");
   if(target.exists()&&!target.delete())throw new IOException("Не удалось удалить старое обновление");if(!partial.renameTo(target))throw new IOException("Не удалось сохранить обновление");
   activity.runOnUiThread(()->{busy=false;if(activity.isDestroyed()||activity.isFinishing())return;activity.getSharedPreferences("app_updates",0).edit().putBoolean("pending",true).apply();installPending(activity);});
  }catch(Exception e){partial.delete();activity.runOnUiThread(()->{busy=false;if(!activity.isDestroyed())Toast.makeText(activity,"Ошибка обновления: "+e.getMessage(),Toast.LENGTH_LONG).show();});}
 });}
 static void installPending(Activity a){android.content.SharedPreferences prefs=a.getSharedPreferences("app_updates",0);if(!prefs.getBoolean("pending",false))return;
  if(!a.getPackageManager().canRequestPackageInstalls()){prefs.edit().putBoolean("pending",false).apply();new AlertDialog.Builder(a).setTitle("Разрешение на установку").setMessage("Разрешите Aster VPN устанавливать обновления. Затем повторите проверку обновлений.").setPositiveButton("Настройки",(d,w)->a.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+a.getPackageName())))).setNegativeButton("Позже",null).show();return;}
  prefs.edit().putBoolean("pending",false).apply();try{a.startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse("content://"+a.getPackageName()+".updates/update.apk"),"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));}catch(Exception e){Toast.makeText(a,"Установщик недоступен: "+e.getMessage(),Toast.LENGTH_LONG).show();}
 }
}
