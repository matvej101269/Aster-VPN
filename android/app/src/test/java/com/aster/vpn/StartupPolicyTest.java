package com.aster.vpn;
import org.json.*;
import org.junit.Test;
import java.util.*;
import static org.junit.Assert.*;
public class StartupPolicyTest {
 private JSONObject data(int count)throws Exception{JSONArray servers=new JSONArray();for(int i=0;i<count;i++)servers.put(new JSONObject().put("id","s"+i));return new JSONObject().put("subscriptions",new JSONArray().put(new JSONObject().put("servers",servers))).put("selected","s0");}
 @Test public void includesAllServersBeyondTwenty()throws Exception{assertEquals(75,StartupPolicy.servers(data(75)).size());}
 @Test public void lastUsedIsNotCurrentSelection()throws Exception{JSONObject d=data(2).put("lastConnection",new JSONObject().put("id","s1"));assertEquals("s1",StartupPolicy.choose(d,0,Collections.emptyMap(),new Random(1)).getString("id"));}
 @Test public void beforeFirstConnectionUsesSelection()throws Exception{assertEquals("s0",StartupPolicy.choose(data(2),0,Collections.emptyMap(),new Random()).getString("id"));}
 @Test public void fastestIgnoresFailedAndUnmeasured()throws Exception{Map<String,Long> delays=new HashMap<>();delays.put("s0",-1L);delays.put("s1",50L);delays.put("s2",10L);assertEquals("s2",StartupPolicy.choose(data(4),1,delays,new Random()).getString("id"));}
 @Test public void noPingResponseDoesNotSelectArbitraryServer()throws Exception{assertNull(StartupPolicy.choose(data(3),1,Collections.singletonMap("s0",-1L),new Random()));}
 @Test public void randomCanChooseServersBeyondTwenty()throws Exception{Set<String> seen=new HashSet<>();JSONObject d=data(75);Random random=new Random(7);for(int i=0;i<2000;i++){String id=StartupPolicy.choose(d,2,Collections.emptyMap(),random).getString("id");assertNotNull(Store.find(d,id));seen.add(id);}assertEquals(75,seen.size());}
 @Test public void emptySubscriptionDoesNotCrash()throws Exception{for(int mode=0;mode<3;mode++)assertNull(StartupPolicy.choose(data(0),mode,Collections.emptyMap(),new Random()));}
 @Test public void invalidSavedModeFallsBackToLast()throws Exception{assertEquals(0,StartupPolicy.mode(new JSONObject().put("connectOnOpenMode",99)));}
}
