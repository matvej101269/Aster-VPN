package com.aster.vpn;
import org.json.*;
public final class XraySettings {
 public static final int MTU=1280;
 public static String build(JSONObject profile,JSONObject preferences)throws Exception{return build(profile,preferences,null);}
 public static String build(JSONObject profile,JSONObject preferences,JSONObject proxyOverride)throws Exception{
  JSONObject outbound=new JSONObject((proxyOverride!=null?proxyOverride:profile.getJSONObject("outbound")).toString());outbound.put("tag","proxy");TransportSecurity.harden(outbound);
  int siteMode=preferences.optInt("siteMode"),geoMode=preferences.optInt("geoMode");JSONArray sites=RoutingRules.list(preferences,"sites",siteMode),countries=RoutingRules.list(preferences,"countries",geoMode);boolean useSites=siteMode!=0&&sites.length()>0;
  JSONObject inbound=new JSONObject().put("tag","tun").put("protocol","tun").put("settings",new JSONObject().put("name","aster0").put("mtu",MTU));
  if(useSites)inbound.put("sniffing",new JSONObject().put("enabled",true).put("destOverride",new JSONArray().put("fakedns").put("http").put("tls")).put("metadataOnly",false));
  JSONArray rules=new JSONArray();rules.put(field("proxy").put("inboundTag",new JSONArray().put("dns-internal")));
  rules.put(field("dns-out").put("inboundTag",new JSONArray().put("tun")).put("port","53"));
  rules.put(field("block").put("ip",new JSONArray().put("::/0")));
  if(preferences.optBoolean("bypassLan",true))rules.put(field("direct").put("ip",new JSONArray(java.util.Arrays.asList(LanRoutes.LOCAL))));
  if(useSites){JSONArray domains=new JSONArray();for(int i=0;i<sites.length();i++)domains.put("domain:"+sites.getString(i));rules.put(field(siteMode==1?"proxy":"direct").put("domain",domains));}
  if(geoMode!=0&&countries.length()>0){JSONArray ips=new JSONArray();for(int i=0;i<countries.length();i++)ips.put("geoip:"+countries.getString(i));rules.put(field(geoMode==1?"proxy":"direct").put("ip",ips));}
  rules.put(field(siteMode==1||geoMode==1?"direct":"proxy").put("network","tcp,udp"));
  JSONArray dnsServers=new JSONArray();if(useSites)dnsServers.put("fakedns");dnsServers.put(new JSONObject().put("address","https://1.1.1.1/dns-query").put("timeoutMs",3000));dnsServers.put(new JSONObject().put("address","https://8.8.8.8/dns-query").put("timeoutMs",3000));
  JSONObject dns=new JSONObject().put("tag","dns-internal").put("queryStrategy","UseIPv4").put("enableParallelQuery",true).put("servers",dnsServers);
  JSONObject direct=new JSONObject().put("tag","direct").put("protocol","freedom").put("settings",new JSONObject().put("domainStrategy","UseIPv4"));
  JSONObject config=new JSONObject().put("stats",new JSONObject()).put("policy",new JSONObject().put("system",new JSONObject().put("statsOutboundUplink",true).put("statsOutboundDownlink",true))).put("log",new JSONObject().put("loglevel","none")).put("dns",dns)
   .put("inbounds",new JSONArray().put(inbound))
   .put("outbounds",new JSONArray().put(outbound).put(direct).put(new JSONObject().put("tag","block").put("protocol","blackhole")).put(new JSONObject().put("tag","dns-out").put("protocol","dns")))
   .put("routing",new JSONObject().put("domainStrategy",geoMode!=0?"IPOnDemand":"AsIs").put("rules",rules));
  if(useSites)config.put("fakedns",new JSONArray().put(new JSONObject().put("ipPool","198.18.0.0/15").put("poolSize",65535)));
  return config.toString();
 }
 private static JSONObject field(String tag)throws Exception{return new JSONObject().put("type","field").put("outboundTag",tag);}
 public static JSONObject awgOutbound(int port,String password)throws Exception{return new JSONObject().put("protocol","socks").put("settings",new JSONObject().put("servers",new JSONArray().put(new JSONObject().put("address","127.0.0.1").put("port",port).put("users",new JSONArray().put(new JSONObject().put("user","aster").put("pass",password))))));}
}
