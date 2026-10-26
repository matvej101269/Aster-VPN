package com.aster.vpn;
import org.json.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

public final class Profiles {
 public static JSONArray parse(String text) throws Exception { return parse(text,0); }
 private static JSONArray parse(String input,int depth) throws Exception {
  if(depth>6)throw new IllegalArgumentException("Слишком много вложений подписки");
  String text=input.trim().replace("\uFEFF","");
  if(text.contains("[Interface]"))return new JSONArray().put(profile("AmneziaWG","amneziawg",text,null));
  if(!text.contains("://"))return parse(decode(text),depth+1);
  JSONArray result=new JSONArray(); int line=0;
  for(String raw:text.split("\\r?\\n")){
   line++;String s=raw.trim();if(s.isEmpty()||s.startsWith("#")||s.startsWith("tg://"))continue;
   try{result.put(uri(s,depth));}catch(Exception e){throw new IllegalArgumentException("Строка "+line+": неверный или неподдерживаемый профиль ("+e.getClass().getSimpleName()+")");}
  }
  if(result.length()==0)throw new IllegalArgumentException("В подписке нет серверов");return result;
 }
 public static String decode(String s){return new String(Base64.getDecoder().decode(s.replace('-','+').replace('_','/').replaceAll("\\s", "")),StandardCharsets.UTF_8);}
 private static String unescape(String s)throws Exception{return URLDecoder.decode(s.replace("+","%2B"),"UTF-8");}
 private static JSONObject profile(String name,String protocol,String raw,JSONObject outbound)throws Exception{
  StringBuilder digest=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(raw.getBytes(StandardCharsets.UTF_8)))digest.append(String.format(Locale.ROOT,"%02x",b));String id=digest.toString();
  JSONObject p=new JSONObject().put("id",id).put("name",name).put("protocol",protocol);
  if(outbound==null)p.put("config",raw);else p.put("outbound",outbound);return p;
 }
 private static JSONObject uri(String raw,int depth)throws Exception{
  String scheme=raw.substring(0,raw.indexOf(':')).toLowerCase(Locale.ROOT);
  if(Arrays.asList("awg","amneziawg","vpn","wg","wireguard").contains(scheme)){
   String[] parts=raw.substring(raw.indexOf("://")+3).split("#",2);String conf=decode(parts[0]);
   if(!conf.contains("[Interface]"))throw new IllegalArgumentException("Нужен WireGuard conf");
   String name=parts.length>1?unescape(parts[1]):"AmneziaWG";
   for(String l:conf.split("\\n"))if(l.trim().startsWith("# Name="))name=l.trim().substring(7).trim();
   return profile(name,"amneziawg",conf,null);
  }
  if(scheme.equals("vmess")){
   JSONObject v=new JSONObject(decode(raw.substring(8))); JSONObject user=new JSONObject().put("id",v.getString("id")).put("alterId",v.optInt("aid")).put("security","auto");
   Map<String,String> q=new HashMap<>();q.put("type",v.optString("net","tcp"));q.put("security",v.optString("tls").equals("tls")?"tls":"none");q.put("sni",v.optString("sni",v.getString("add")));q.put("path",v.optString("path","/"));q.put("host",v.optString("host"));
   JSONObject out=new JSONObject().put("protocol","vmess").put("tag","proxy").put("settings",new JSONObject().put("vnext",new JSONArray().put(new JSONObject().put("address",v.getString("add")).put("port",Integer.parseInt(v.getString("port"))).put("users",new JSONArray().put(user))))).put("streamSettings",stream(q,v.getString("add")));
   return profile(v.optString("ps","VMess"),"vmess",raw,out);
  }
  URI u=new URI(raw);Map<String,String> q=new HashMap<>();if(u.getRawQuery()!=null)for(String pair:u.getRawQuery().split("&")){String[] kv=pair.split("=",2);q.put(unescape(kv[0]),kv.length>1?unescape(kv[1]):"");}
  if(u.getHost()==null||u.getPort()<1)throw new IllegalArgumentException("Нет адреса или порта");
  String user=unescape(u.getRawUserInfo()==null?"":u.getRawUserInfo());
  JSONObject server=new JSONObject().put("address",u.getHost()).put("port",u.getPort()); JSONObject settings=new JSONObject();
  switch(scheme){
   case "vless":
    JSONObject auth=new JSONObject().put("id",user).put("encryption",q.getOrDefault("encryption","none"));if(q.containsKey("flow"))auth.put("flow",q.get("flow"));server.put("users",new JSONArray().put(auth));settings.put("vnext",new JSONArray().put(server));break;
   case "trojan":server.put("password",user);settings.put("servers",new JSONArray().put(server));q.putIfAbsent("security","tls");break;
   case "ss":String cred=user.contains(":")?user:decode(user);String[] cp=cred.split(":",2);server.put("method",cp[0]).put("password",cp[1]);settings.put("servers",new JSONArray().put(server));scheme="shadowsocks";break;
   case "socks":case "socks5":case "http":String[] up=user.split(":",2);if(!user.isEmpty())server.put("users",new JSONArray().put(new JSONObject().put("user",up[0]).put("pass",up.length>1?up[1]:"")));settings.put("servers",new JSONArray().put(server));if(scheme.equals("socks5"))scheme="socks";break;
   default:throw new IllegalArgumentException("Протокол не поддерживается");
  }
  JSONObject out=new JSONObject().put("protocol",scheme).put("tag","proxy").put("settings",settings).put("streamSettings",stream(q,u.getHost()));
  return profile(u.getRawFragment()==null?scheme.toUpperCase(Locale.ROOT):unescape(u.getRawFragment()),scheme,raw,out);
 }
 private static JSONObject stream(Map<String,String> q,String host)throws Exception{
  if(Arrays.asList("true","1").contains(q.getOrDefault("allowInsecure",""))||Arrays.asList("true","1").contains(q.getOrDefault("insecure","")))throw new IllegalArgumentException("Проверку TLS отключать нельзя");
  String type=q.getOrDefault("type","tcp"),security=q.getOrDefault("security","none");if(type.equals("tcp"))type="raw";if(type.equals("splithttp"))type="xhttp";
  JSONObject s=new JSONObject().put("network",type).put("security",security);String path=q.getOrDefault("path","/");
  switch(type){
   case "raw":break;
   case "xhttp":JSONObject x=new JSONObject().put("path",path).put("host",q.getOrDefault("host","")).put("mode",q.getOrDefault("mode","auto"));String extra=q.getOrDefault("extra","");if(!extra.trim().isEmpty()&&!extra.trim().equals("null"))x.put("extra",new JSONObject(extra));s.put("xhttpSettings",x);break;
   case "ws":s.put("wsSettings",new JSONObject().put("path",path).put("headers",new JSONObject().put("Host",q.getOrDefault("host",host))));break;
   case "grpc":s.put("grpcSettings",new JSONObject().put("serviceName",q.getOrDefault("serviceName","")));break;
   case "httpupgrade":s.put("httpupgradeSettings",new JSONObject().put("path",path).put("host",q.getOrDefault("host",host)));break;
   default:throw new IllegalArgumentException("Транспорт не поддерживается");
  }
  if(security.equals("tls")||security.equals("reality")){
   JSONObject tls=new JSONObject().put("serverName",q.getOrDefault("sni",host)).put("fingerprint",q.getOrDefault("fp","chrome"));
   if(security.equals("reality"))tls.put("publicKey",q.getOrDefault("pbk","")).put("shortId",q.getOrDefault("sid","")).put("spiderX",q.getOrDefault("spx","/"));
   else {tls.put("allowInsecure",false);if(q.containsKey("alpn"))tls.put("alpn",new JSONArray(Arrays.asList(q.get("alpn").split(","))));}
   s.put(security+"Settings",tls);
  }else if(!security.equals("none"))throw new IllegalArgumentException("Неизвестный TLS режим");
  return s;
 }
}
