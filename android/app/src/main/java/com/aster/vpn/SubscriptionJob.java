package com.aster.vpn;
import android.app.job.*;
import android.content.*;
import android.os.*;
import org.json.*;
public final class SubscriptionJob extends JobService {
 private volatile Run current;
 private static final class Run {volatile boolean cancelled;Thread thread;}
 public static void schedule(Context c){JobScheduler scheduler=c.getSystemService(JobScheduler.class);if(scheduler.getPendingJob(20)!=null)return;scheduler.schedule(new JobInfo.Builder(20,new ComponentName(c,SubscriptionJob.class)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setRequiresBatteryNotLow(true).setPeriodic(60*60*1000L,30*60*1000L).build());}
 @Override public boolean onStartJob(JobParameters p){Run run=new Run();current=run;run.thread=new Thread(()->{android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_BACKGROUND);try{Store store=new Store(this);JSONObject data=store.read();if(data.optBoolean("autoUpdate",true)){JSONArray subs=data.getJSONArray("subscriptions");for(int i=0;i<subs.length()&&!run.cancelled;i++){JSONObject sub=subs.getJSONObject(i);if(!sub.optString("url").isEmpty()&&System.currentTimeMillis()-sub.optLong("updated")>=sub.optInt("hours",6)*3600000L)try{JSONObject fetched=Subscriptions.fetch(this,sub.getString("url"));if(!run.cancelled)Subscriptions.merge(store,fetched,sub.getString("id"));}catch(Exception ignored){}}}}catch(Exception ignored){}finally{new Handler(Looper.getMainLooper()).post(()->{if(current==run&&!run.cancelled){current=null;jobFinished(p,false);}});}},"Aster subscriptions");run.thread.start();return true;}
 @Override public boolean onStopJob(JobParameters p){cancel();return true;}
 private void cancel(){Run run=current;current=null;if(run!=null){run.cancelled=true;run.thread.interrupt();}}
 @Override public void onDestroy(){cancel();super.onDestroy();}
}
