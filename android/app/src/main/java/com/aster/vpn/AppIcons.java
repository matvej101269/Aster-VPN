package com.aster.vpn;
import android.content.Context;
import android.graphics.*;
import android.graphics.drawable.Drawable;
import android.os.*;
import android.util.LruCache;
import android.widget.ImageView;
import java.util.concurrent.*;
/** Only visible rows decode icons. Retain at most 1 MiB of small rasters. */
final class AppIcons {
 private static final LruCache<String,Bitmap> cache=new LruCache<String,Bitmap>(1024*1024){protected int sizeOf(String key,Bitmap b){return b.getAllocationByteCount();}};
 private static final ThreadPoolExecutor worker=new ThreadPoolExecutor(1,1,15,TimeUnit.SECONDS,new ArrayBlockingQueue<>(32),r->{Thread t=new Thread(()->{android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_BACKGROUND);r.run();},"aster-icons");return t;},new ThreadPoolExecutor.DiscardPolicy());
 private static final Handler main=new Handler(Looper.getMainLooper());
 static {worker.allowCoreThreadTimeOut(true);}
 static void bind(Context context,String pkg,ImageView view){view.setTag(pkg);Bitmap cached=cache.get(pkg);view.setImageBitmap(cached);if(cached!=null)return;Context app=context.getApplicationContext();java.lang.ref.WeakReference<ImageView> target=new java.lang.ref.WeakReference<>(view);worker.execute(()->{Bitmap bitmap=cache.get(pkg);if(bitmap==null)try{Drawable icon=app.getPackageManager().getApplicationIcon(pkg);int size=Math.min(96,Math.round(32*app.getResources().getDisplayMetrics().density));bitmap=Bitmap.createBitmap(size,size,Bitmap.Config.ARGB_8888);icon.setBounds(0,0,size,size);icon.draw(new Canvas(bitmap));cache.put(pkg,bitmap);}catch(Exception ignored){}Bitmap loaded=bitmap;main.post(()->{ImageView image=target.get();if(image!=null&&pkg.equals(image.getTag())&&image.isAttachedToWindow())image.setImageBitmap(loaded);});});}
 static void trim(){worker.getQueue().clear();cache.evictAll();}
}
