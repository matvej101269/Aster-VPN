package com.aster.vpn;
import android.content.*;
import android.content.pm.*;

import android.os.*;
import java.util.*;
import java.util.concurrent.*;

/** The package manager and icon decoding never run on the UI thread. */
public final class AppCatalog {
 public static final class Entry {public final String pkg,label;Entry(String p,String l){pkg=p;label=l;}}
 private static final ExecutorService worker=IdleWorkers.single("aster-app-catalog");private static final Handler main=new Handler(Looper.getMainLooper());private static List<Entry> cached;private static long loaded;private static boolean loading;private static final List<Callback> waiting=new ArrayList<>();
 public static synchronized void trim(){cached=null;loaded=0;}
 public interface Callback {void done(List<Entry> entries);}
 public static synchronized void load(Context context,Callback callback){if(cached!=null&&SystemClock.elapsedRealtime()-loaded<300000){List<Entry> snapshot=cached;if(callback!=null)main.post(()->callback.done(snapshot));return;}if(callback!=null)waiting.add(callback);if(loading)return;loading=true;Context app=context.getApplicationContext();worker.execute(()->{android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_BACKGROUND);ArrayList<Entry> result=new ArrayList<>();try{PackageManager pm=app.getPackageManager();Set<String> shown=new HashSet<>();for(ResolveInfo info:pm.queryIntentActivities(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER),0)){String pkg=info.activityInfo.packageName;if(pkg.equals(app.getPackageName())||!shown.add(pkg))continue;try{result.add(new Entry(pkg,info.loadLabel(pm).toString()));}catch(RuntimeException ignored){}}result.sort(Comparator.comparing(e->e.label.toLowerCase(Locale.ROOT)));}finally{List<Entry> entries=Collections.unmodifiableList(result);List<Callback> callbacks;synchronized(AppCatalog.class){cached=entries;loaded=SystemClock.elapsedRealtime();loading=false;callbacks=new ArrayList<>(waiting);waiting.clear();}main.post(()->{for(Callback cb:callbacks)cb.done(entries);});}});}
}
