package com.aster.vpn;
import org.json.*;
import org.junit.Test;
import static org.junit.Assert.*;
public class RoutingTest {
 private JSONObject profile()throws Exception{return Profiles.parse("vless://f1f1408b-959e-4c09-bb5b-981c8ba33101@example.com:443?type=xhttp&security=tls#Test").getJSONObject(0);}
 @Test public void domainNormalization()throws Exception{assertEquals("[\"youtube.com\",\"xn--e1afmkfd.xn--p1ai\"]",RoutingRules.sites("https://YouTube.com/watch?v=1 *.youtube.com пример.рф").toString());}
 @Test(expected=IllegalArgumentException.class)public void rejectIpAsDomain()throws Exception{RoutingRules.sites("1.1.1.1");}
 @Test(expected=IllegalArgumentException.class)public void rejectUnsupportedCountry(){RoutingRules.countries("RU XX");}
 @Test public void geoDeduplication(){assertEquals("[\"ru\",\"de\"]",RoutingRules.countries("RU de ru").toString());}
 @Test public void flagsAppearOnce(){assertEquals("🇩🇪",ServerNames.flag("🇩🇪 Германия"));assertEquals("Германия — #1",ServerNames.display("🇩🇪Германия — #1"));assertEquals("Server",ServerNames.display("Server"));}
 @Test public void includeSitesUsesFakeDnsAndDefaultDirect()throws Exception{JSONObject prefs=new JSONObject().put("siteMode",1).put("sitesVpn",new JSONArray().put("youtube.com"));JSONObject c=new JSONObject(XraySettings.build(profile(),prefs));assertTrue(c.has("fakedns"));assertEquals("fakedns",c.getJSONObject("dns").getJSONArray("servers").getString(0));JSONArray rules=c.getJSONObject("routing").getJSONArray("rules");assertEquals("direct",rules.getJSONObject(rules.length()-1).getString("outboundTag"));boolean found=false;for(int i=0;i<rules.length();i++){JSONObject r=rules.getJSONObject(i);if(r.has("domain")){found=true;assertEquals("domain:youtube.com",r.getJSONArray("domain").getString(0));assertEquals("proxy",r.getString("outboundTag"));}}assertTrue(found);}
 @Test public void inactiveSiteListsDoNotEnableFakeDns()throws Exception{JSONObject p=new JSONObject().put("sitesDirect",new JSONArray().put("example.com"));assertFalse(new JSONObject(XraySettings.build(profile(),p)).has("fakedns"));}
 @Test public void countriesUseSameEngineForAwg()throws Exception{JSONObject prefs=new JSONObject().put("geoMode",2).put("countriesDirect",new JSONArray().put("ru"));JSONObject c=new JSONObject(XraySettings.build(new JSONObject(),prefs,XraySettings.awgOutbound(12345,"secret")));assertEquals("socks",c.getJSONArray("outbounds").getJSONObject(0).getString("protocol"));assertEquals("IPOnDemand",c.getJSONObject("routing").getString("domainStrategy"));assertTrue(c.toString().contains("geoip:ru"));}
 @Test public void tcpAndUdpAreAllowedIncludingLegacyVideoPreference()throws Exception{
  for(JSONObject prefs:new JSONObject[]{new JSONObject(),new JSONObject().put("videoTcp",true),new JSONObject().put("videoTcp",false)}){
   JSONArray rules=new JSONObject(XraySettings.build(profile(),prefs)).getJSONObject("routing").getJSONArray("rules");
   for(int i=0;i<rules.length();i++){JSONObject r=rules.getJSONObject(i);assertFalse("UDP 443 must not be filtered",r.optString("port").equals("443")&&r.optString("network").equals("udp"));}
   JSONObject fallback=rules.getJSONObject(rules.length()-1);assertEquals("tcp,udp",fallback.getString("network"));assertEquals("proxy",fallback.getString("outboundTag"));
  }
 }
}
