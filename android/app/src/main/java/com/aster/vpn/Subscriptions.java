package com.aster.vpn;
import android.content.Context;
import android.os.Build;
import org.json.*;
import java.net.*;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

public final class Subscriptions {
 public static JSONObject fetch(Context context,String address)throws Exception {
  URL url=new URL(address.trim()); validate(url);String origin=origin(url);
  for(int redirects=0;redirects<6;redirects++){
   HttpsURLConnection c=(HttpsURLConnection)url.openConnection();c.setConnectTimeout(15000);c.setReadTimeout(20000);c.setInstanceFollowRedirects(false);
   c.setRequestProperty("User-Agent","AsterVPN/"+BuildConfig.VERSION_NAME+" Android");
   if(origin(url).equals(origin)){c.setRequestProperty("x-hwid",Identity.hwid(context));c.setRequestProperty("x-device-os","Android");c.setRequestProperty("x-ver-os",Build.VERSION.RELEASE);c.setRequestProperty("x-device-model",Build.MANUFACTURER+" "+Build.MODEL);c.setRequestProperty("x-client","AsterVPN");c.setRequestProperty("x-app-version",BuildConfig.VERSION_NAME);}
   try{
    int status=c.getResponseCode();if(status>=300&&status<400){String location=c.getHeaderField("Location");if(location==null)throw new IOException("Пустое перенаправление");url=new URL(url,location);validate(url);continue;}
    if(status!=200)throw new IOException("Сервер подписки вернул HTTP "+status);
    String body=read(c.getInputStream());JSONArray servers=Profiles.parse(body);
    String title=c.getHeaderField("Profile-Title");if(title==null||title.trim().isEmpty())title=url.getHost();else if(title.startsWith("base64:"))title=Profiles.decode(title.substring(7));
    int hours=6;try{hours=Math.max(1,Math.min(168,Integer.parseInt(c.getHeaderField("Profile-Update-Interval"))));}catch(Exception ignored){}
    return new JSONObject().put("id",UUID.randomUUID().toString()).put("url",address.trim()).put("name",title).put("servers",servers).put("updated",System.currentTimeMillis()).put("hours",hours).put("usage",Objects.toString(c.getHeaderField("Subscription-Userinfo"),""));
   }finally{c.disconnect();}
  }throw new IOException("Слишком много перенаправлений");
 }
 static String read(InputStream in)throws IOException{try(in;ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] buf=new byte[8192];int n;while((n=in.read(buf))!=-1){if(out.size()+n>8*1024*1024)throw new IOException("Подписка больше 8 МБ");out.write(buf,0,n);}return out.toString("UTF-8");}}
 private static String origin(URL u){return u.getProtocol()+"://"+u.getHost().toLowerCase(Locale.ROOT)+":"+u.getPort();}
 private static void validate(URL u)throws IOException{if(!u.getProtocol().equals("https")||u.getUserInfo()!=null)throw new IOException("Нужна HTTPS-ссылка без логина в адресе");}
 public static void merge(Store store,JSONObject sub,String replaceId)throws Exception{
  store.change(data->{JSONArray all=data.getJSONArray("subscriptions");boolean replaced=false;for(int i=0;i<all.length();i++)if(all.getJSONObject(i).optString("id").equals(replaceId)){sub.put("id",replaceId);all.put(i,sub);replaced=true;break;}if(!replaced)all.put(sub);
   if(Store.find(data,data.optString("selected"))==null)data.put("selected",sub.getJSONArray("servers").getJSONObject(0).getString("id"));
   JSONObject last=data.optJSONObject("lastConnection");if(last!=null){JSONObject refreshed=Store.find(data,last.optString("id"));if(refreshed!=null)data.put("lastConnection",refreshed);}
  });
 }
}
