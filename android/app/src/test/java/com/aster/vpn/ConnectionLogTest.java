package com.aster.vpn;
import org.json.*;
import org.junit.Test;
import static org.junit.Assert.*;
public class ConnectionLogTest {
 private static final long NOW=1800000000000L;
 private JSONObject row(long time,String message)throws Exception{return new JSONObject().put("time",time).put("message",message);}
 @Test public void removesEntriesAt24HoursAndOlder()throws Exception{JSONArray rows=new JSONArray().put(row(NOW-ConnectionLog.RETENTION_MS-1,"old")).put(row(NOW-ConnectionLog.RETENTION_MS,"boundary")).put(row(NOW-ConnectionLog.RETENTION_MS+1,"recent"));JSONArray result=ConnectionLog.retained(rows,NOW);assertEquals(1,result.length());assertEquals("recent",result.getJSONObject(0).getString("message"));}
 @Test public void keepsOnlyLatest200Entries()throws Exception{JSONArray rows=new JSONArray();for(int i=0;i<250;i++)rows.put(row(NOW,"entry"+i));JSONArray result=ConnectionLog.retained(rows,NOW);assertEquals(200,result.length());assertEquals("entry50",result.getJSONObject(0).getString("message"));assertEquals("entry249",result.getJSONObject(199).getString("message"));}
 @Test public void clearsLegacyAndInvalidOrFutureEntries()throws Exception{JSONArray rows=new JSONArray().put("26.10 10:00 legacy").put(new JSONObject()).put(row(NOW+1,"future")).put(row(NOW,""));assertEquals(0,ConnectionLog.retained(rows,NOW).length());}
 @Test public void boundsMessageSize()throws Exception{JSONArray result=ConnectionLog.retained(new JSONArray().put(row(NOW,"x".repeat(5000))),NOW);assertEquals(1024,result.getJSONObject(0).getString("message").length());}
 @Test public void noActivityFor24HoursLeavesEmptyLog()throws Exception{JSONArray rows=new JSONArray().put(row(NOW,"last"));assertEquals(0,ConnectionLog.retained(rows,NOW+ConnectionLog.RETENTION_MS).length());}
}
