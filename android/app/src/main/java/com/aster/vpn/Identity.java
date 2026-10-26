package com.aster.vpn;
import android.content.Context;
import android.provider.Settings;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Locale;
public final class Identity {
 public static String xudpKey(String deviceId) throws Exception {
  return java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(MessageDigest.getInstance("SHA-256").digest(("AsterVPN:XUDP:v1:"+deviceId).getBytes(StandardCharsets.UTF_8)));
 }
 public static String hwid(Context c) throws Exception {
  String id=Settings.Secure.getString(c.getContentResolver(),Settings.Secure.ANDROID_ID);
  if(id==null || id.isEmpty())throw new IllegalStateException("Android ID недоступен");
  byte[] bytes=MessageDigest.getInstance("SHA-256").digest(("AsterVPN:Android:v1:"+id).getBytes(StandardCharsets.UTF_8));
  StringBuilder s=new StringBuilder(); for(int i=0;i<16;i++){if(i==4||i==6||i==8||i==10)s.append('-');s.append(String.format(Locale.ROOT,"%02X",bytes[i]));}return s.toString();
 }
}
