package com.aster.vpn;
import org.json.*;
import java.util.*;
public final class StartupPolicy {
 public static final String[] LABELS={"Последний использованный","С наименьшей задержкой","Случайный"};
 public static List<JSONObject> servers(JSONObject data)throws JSONException{List<JSONObject> result=new ArrayList<>();Set<String> ids=new HashSet<>();JSONArray subs=data.optJSONArray("subscriptions");if(subs!=null)for(int i=0;i<subs.length();i++){JSONArray profiles=subs.getJSONObject(i).getJSONArray("servers");for(int j=0;j<profiles.length();j++){JSONObject p=profiles.getJSONObject(j);if(ids.add(p.getString("id")))result.add(p);}}return result;}
 public static JSONObject choose(JSONObject data,int mode,Map<String,Long> delays,Random random)throws JSONException{
  if(mode==0)return Store.connection(data,true);
  List<JSONObject> all=servers(data);if(all.isEmpty())return null;
  if(mode==2)return all.get(random.nextInt(all.size()));
  JSONObject best=null;long minimum=Long.MAX_VALUE;for(JSONObject p:all){Long ms=delays.get(p.optString("id"));if(ms!=null&&ms>=0&&ms<minimum){minimum=ms;best=p;}}return best;
 }
 public static int mode(JSONObject data){int value=data.optInt("connectOnOpenMode");return value>=0&&value<LABELS.length?value:0;}
}
