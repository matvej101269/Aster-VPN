package com.aster.vpn;
import java.net.*;
import java.util.*;
import org.json.*;

/** Validation shared by the editor and config builder. No profile IDs or keys change. */
public final class RoutingRules {
 public static JSONArray sites(String input)throws Exception{LinkedHashSet<String> values=new LinkedHashSet<>();for(String item:input.split("[\\s,;]+")){if(item.isBlank())continue;String host=item.trim();if(host.contains("://")){URI u=new URI(host);host=u.getHost();if(host==null){String a=u.getRawAuthority();if(a!=null&&!a.contains("@"))host=a.split(":",2)[0];}}else host=host.split("/",2)[0];if(host==null)throw new IllegalArgumentException("Некорректный сайт");if(host.startsWith("*."))host=host.substring(2);host=IDN.toASCII(host,IDN.USE_STD3_ASCII_RULES).toLowerCase(Locale.ROOT).replaceAll("\\.$","");if(host.startsWith("*."))host=host.substring(2);if(host.length()>253||!host.contains(".")||host.matches("[0-9.]+")||host.contains(":")||host.contains("@"))throw new IllegalArgumentException("Укажите домен сайта без IP-адреса и порта");for(String label:host.split("\\."))if(label.isEmpty()||label.length()>63||label.startsWith("-")||label.endsWith("-"))throw new IllegalArgumentException("Некорректный домен");values.add(host);}return new JSONArray(values);}
 public static JSONArray countries(String input){Set<String> allowed=new HashSet<>(Arrays.asList(Locale.getISOCountries()));LinkedHashSet<String> values=new LinkedHashSet<>();for(String item:input.split("[\\s,;]+")){if(item.isBlank())continue;String code=item.toUpperCase(Locale.ROOT);if(!allowed.contains(code))throw new IllegalArgumentException("Неизвестный код страны: "+item);values.add(code.toLowerCase(Locale.ROOT));}return new JSONArray(values);}
 public static JSONArray list(JSONObject prefs,String name,int mode){JSONArray a=prefs.optJSONArray(name+(mode==1?"Vpn":"Direct"));return a==null?new JSONArray():a;}
 public static String lines(JSONArray list){ArrayList<String> values=new ArrayList<>();for(int i=0;i<list.length();i++)values.add(list.optString(i));return String.join("\n",values);}
 public static boolean xhttp(JSONObject p){JSONObject out=p.optJSONObject("outbound"),s=out==null?null:out.optJSONObject("streamSettings");return s!=null&&s.optString("network").equals("xhttp");}
}
