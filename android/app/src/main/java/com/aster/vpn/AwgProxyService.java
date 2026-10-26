package com.aster.vpn;
import android.app.Service;
import android.content.Intent;
import android.os.*;
import java.util.concurrent.*;
/** A separate process keeps the two independently built Go runtimes isolated. */
public final class AwgProxyService extends Service {
 private static final ExecutorService worker=Executors.newSingleThreadExecutor();
 private final Messenger messenger=new Messenger(new Handler(Looper.getMainLooper(),message->{if(message.what==1){String config=message.getData().getString("config");Messenger reply=message.replyTo;worker.execute(()->{Message result=Message.obtain(null,1);ParcelFileDescriptor v4=null,v6=null;try{int port=AwgProxy.start(config);Bundle data=new Bundle();data.putInt("port",port);if(port>0){int fd4=AwgProxy.socket(4),fd6=AwgProxy.socket(6);if(fd4>=0){v4=ParcelFileDescriptor.fromFd(fd4);data.putParcelable("v4",v4);}if(fd6>=0){v6=ParcelFileDescriptor.fromFd(fd6);data.putParcelable("v6",v6);}}result.setData(data);reply.send(result);}catch(Exception e){try{result.arg1=-1;reply.send(result);}catch(Exception ignored){}}finally{try{if(v4!=null)v4.close();if(v6!=null)v6.close();}catch(Exception ignored){}}});}else if(message.what==2)worker.execute(AwgProxy::stop);return true;}));
 @Override public IBinder onBind(Intent intent){return messenger.getBinder();}
 @Override public void onDestroy(){worker.execute(AwgProxy::stop);super.onDestroy();}
}
