package com.aster.vpn;
import android.content.Context;
import org.json.*;
import java.text.SimpleDateFormat;
import java.util.*;
/** App-private history limited to 24 hours, 200 entries and 1024 chars per message. */
public final class ConnectionLog {
 static final long RETENTION_MS=24*60*60*1000L;
 static final int MAX_ENTRIES=200,MAX_MESSAGE=1024;
 static JSONArray retained(JSONArray old,long now)throws JSONException {
  JSONArray next=new JSONArray();
  for(int i=0;i<old.length();i++){
   JSONObject row=old.optJSONObject(i);if(row==null)continue; // Discard legacy strings without a reliable timestamp.
   long time=row.optLong("time",-1);String message=row.optString("message","");
   if(time<=now-RETENTION_MS||time>now||message.isEmpty())continue;
   next.put(new JSONObject().put("time",time).put("message",message.substring(0,Math.min(message.length(),MAX_MESSAGE))));
  }
  JSONArray bounded=new JSONArray();for(int i=Math.max(0,next.length()-MAX_ENTRIES);i<next.length();i++)bounded.put(next.getJSONObject(i));return bounded;
 }
 private static JSONArray load(Context c,long now)throws JSONException {try{return retained(new JSONArray(c.getSharedPreferences("connection_log",0).getString("events","[]")),now);}catch(JSONException e){return new JSONArray();}}
 private static void save(Context c,JSONArray rows){c.getSharedPreferences("connection_log",0).edit().putString("events",rows.toString()).apply();}
 public static synchronized void prune(Context c){try{save(c,load(c,System.currentTimeMillis()));}catch(Exception ignored){}}
 public static synchronized void add(Context c,String message){try{long now=System.currentTimeMillis();JSONArray rows=load(c,now);rows.put(new JSONObject().put("time",now).put("message",message));save(c,retained(rows,now));}catch(Exception ignored){}}
 public static synchronized String read(Context c){try{JSONArray rows=load(c,System.currentTimeMillis());save(c,rows);StringBuilder text=new StringBuilder();SimpleDateFormat date=new SimpleDateFormat("dd.MM HH:mm:ss",Locale.getDefault());for(int i=0;i<rows.length();i++){JSONObject row=rows.getJSONObject(i);text.append(date.format(new Date(row.getLong("time")))).append("  ").append(row.getString("message")).append('\n');}return text.length()==0?"Событий за последние 24 часа нет.":text.toString();}catch(Exception e){return "Не удалось прочитать журнал";}}
}
