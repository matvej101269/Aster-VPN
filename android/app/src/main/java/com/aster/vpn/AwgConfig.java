package com.aster.vpn;
import java.net.*;
import java.util.*;

public final class AwgConfig {
 public final List<String> addresses=new ArrayList<>(),dns=new ArrayList<>();
 public int mtu=1280;public String uapi;
 private static final Map<String,String> EXTENSIONS=new LinkedHashMap<>();
 static {
  EXTENSIONS.put("headerprotectionkey","header_protection_key");
  EXTENSIONS.put("contentpaddingaddition","content_padding_addition");
  EXTENSIONS.put("rekeyaftertime","rekey_after_time");EXTENSIONS.put("rekeytimeout","rekey_timeout");
  EXTENSIONS.put("rejectaftertime","reject_after_time");EXTENSIONS.put("keepalivetimeout","keepalive_timeout");
  EXTENSIONS.put("maxhandshakeattempts","max_handshake_attempts");
  EXTENSIONS.put("randomtrailers","random_trailers");EXTENSIONS.put("disablecookies","disable_cookies");
 }
 public static AwgConfig parse(String raw)throws Exception{
  AwgConfig result=new AwgConfig();String section="";Map<String,String> device=new LinkedHashMap<>(),peer=new LinkedHashMap<>();int peerCount=0;
  for(String rawLine:raw.split("\\r?\\n")){
   String line=rawLine.split("#",2)[0].trim();if(line.isEmpty())continue;
   if(line.startsWith("[")){
    section=line.toLowerCase(Locale.ROOT);if(section.equals("[peer]")){if(++peerCount>1)throw new IllegalArgumentException("Поддерживается один Peer в AWG-профиле");}
    else if(!section.equals("[interface]")&&!section.equals("[device]"))throw new IllegalArgumentException("Неизвестная секция AWG");continue;
   }
   String[] pair=line.split("=",2);if(pair.length!=2)throw new IllegalArgumentException("Неверная строка AWG");String name=pair[0].trim().toLowerCase(Locale.ROOT),value=pair[1].trim();
   if(section.equals("[interface]")||section.equals("[device]"))switch(name){
    case "address":result.addresses.addAll(Arrays.asList(value.split(",\\s*")));break;
    case "dns":for(String address:value.split(",\\s*"))if(address.matches("[0-9.]+|[0-9a-fA-F:]+"))result.dns.add(address);break;
    case "mtu":result.mtu=Integer.parseInt(value);if(result.mtu<576||result.mtu>9000)throw new IllegalArgumentException("Неверный MTU AWG");break;
    case "privatekey":device.put("private_key",hex(value));break;
    case "listenport":device.put("listen_port",value);break;
    case "headerprotectionkey":device.put("header_protection_key",hex(value));break;
    case "randomtrailers":case "disablecookies":device.put(EXTENSIONS.get(name),bool(value));break;
    default:if(name.matches("jc|jmin|jmax|s[1-4]|h[1-4]|i[1-5]"))device.put(name,value);else if(EXTENSIONS.containsKey(name))device.put(EXTENSIONS.get(name),value);else throw new IllegalArgumentException("Неизвестный параметр AWG: "+name);
   }else if(section.equals("[peer]")){
    if(!Arrays.asList("publickey","presharedkey","endpoint","allowedips","persistentkeepalive").contains(name))throw new IllegalArgumentException("Неизвестный параметр Peer: "+name);
    peer.put(name,value);
   }else throw new IllegalArgumentException("Неверная секция AWG");
  }
  if(!device.containsKey("private_key")||!peer.containsKey("publickey")||!peer.containsKey("endpoint")||!peer.containsKey("allowedips")||result.addresses.isEmpty())throw new IllegalArgumentException("Неполная конфигурация AWG");
  StringBuilder out=new StringBuilder();for(Map.Entry<String,String> e:device.entrySet())append(out,e.getKey(),e.getValue());
  append(out,"replace_peers","true");append(out,"public_key",hex(peer.get("publickey")));
  if(peer.containsKey("presharedkey"))append(out,"preshared_key",hex(peer.get("presharedkey")));
  URI endpoint=new URI("udp://"+peer.get("endpoint"));InetAddress ip=null;if(endpoint.getHost()!=null)for(InetAddress a:InetAddress.getAllByName(endpoint.getHost()))if(a instanceof Inet4Address){ip=a;break;}
  if(ip==null||endpoint.getPort()<1||endpoint.getPort()>65535)throw new IllegalArgumentException("Нет IPv4 адреса AWG");append(out,"endpoint",ip.getHostAddress()+":"+endpoint.getPort());
  append(out,"replace_allowed_ips","true");for(String cidr:peer.get("allowedips").split(",\\s*"))append(out,"allowed_ip",cidr);
  append(out,"persistent_keepalive_interval",peer.getOrDefault("persistentkeepalive","0"));result.uapi=out.toString();
  if(result.dns.isEmpty())result.dns.add("1.1.1.1");return result;
 }
 private static void append(StringBuilder b,String key,String value){if(value.contains("\n")||value.contains("\r"))throw new IllegalArgumentException("Перенос строки в параметре AWG");b.append(key).append('=').append(value).append('\n');}
 private static String bool(String value){switch(value.toLowerCase(Locale.ROOT)){case "true":case "1":case "yes":case "on":case "enabled":return "true";case "false":case "0":case "no":case "off":case "disabled":return "false";default:throw new IllegalArgumentException("Неверный логический параметр AWG");}}
 private static String hex(String value){if(value.matches("[0-9a-fA-F]{64}"))return value.toLowerCase(Locale.ROOT);byte[] bytes=Base64.getDecoder().decode(value);if(bytes.length!=32)throw new IllegalArgumentException("Ключ AWG должен содержать 32 байта");StringBuilder s=new StringBuilder();for(byte b:bytes)s.append(String.format(Locale.ROOT,"%02x",b));return s.toString();}
}
