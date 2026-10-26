package com.aster.vpn;
import android.content.Context;
import org.json.JSONArray;
import java.text.SimpleDateFormat;
import java.util.*;
/** Bounded app-private event history. Never records URLs, keys or core payloads. */
public final class ConnectionLog {
 public static synchronized void add(Context c,String message){try{android.content.SharedPreferences p=c.getSharedPreferences("connection_log",0);JSONArray old=new JSONArray(p.getString("events","[]")),next=new JSONArray();for(int i=Math.max(0,old.length()-199);i<old.length();i++)next.put(old.getString(i));next.put(new SimpleDateFormat("dd.MM HH:mm:ss",Locale.getDefault()).format(new Date())+"  "+message);p.edit().putString("events",next.toString()).apply();}catch(Exception ignored){}}
 public static synchronized String read(Context c){try{JSONArray rows=new JSONArray(c.getSharedPreferences("connection_log",0).getString("events","[]"));StringBuilder text=new StringBuilder();for(int i=0;i<rows.length();i++)text.append(rows.getString(i)).append('\n');return text.length()==0?"Событий пока нет. Подключите VPN, чтобы увидеть журнал.":text.toString();}catch(Exception e){return "Не удалось прочитать журнал";}}
}
