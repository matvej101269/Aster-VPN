package com.aster.vpn;
import android.content.*;
import android.net.VpnService;
import android.os.*;
import java.util.concurrent.*;
public final class AwgBridgeClient implements ServiceConnection {
 private final VpnService context;private final String config;private final Runnable disconnected;private final CountDownLatch ready=new CountDownLatch(1);private Messenger remote;private volatile int port=-1;private volatile boolean bound,closing;
 public AwgBridgeClient(VpnService c,String config,Runnable lost){context=c;this.config=config;disconnected=lost;}
 public int start()throws Exception{bound=context.bindService(new Intent(context,AwgProxyService.class),this,Context.BIND_AUTO_CREATE|Context.BIND_IMPORTANT);if(!bound)throw new IllegalStateException("Не удалось запустить процесс AWG");if(!ready.await(15,TimeUnit.SECONDS)||port<1)throw new IllegalStateException("Процесс AWG не ответил");return port;}
 @Override public void onServiceConnected(ComponentName name,IBinder binder){if(closing)return;remote=new Messenger(binder);Message request=Message.obtain(null,1);Bundle data=new Bundle();data.putString("config",config);request.setData(data);request.replyTo=new Messenger(new Handler(Looper.getMainLooper(),response->{Bundle reply=response.getData();port=reply.getInt("port",-1);try{for(String key:new String[]{"v4","v6"}){ParcelFileDescriptor fd=reply.getParcelable(key);if(fd!=null)try{if(!context.protect(fd.getFd()))port=-1;}finally{fd.close();}}}catch(Exception e){port=-1;}ready.countDown();return true;}));try{remote.send(request);}catch(RemoteException e){ready.countDown();}}
 @Override public void onServiceDisconnected(ComponentName name){remote=null;ready.countDown();if(!closing)disconnected.run();}
 @Override public void onBindingDied(ComponentName name){onServiceDisconnected(name);}
 public void close(){closing=true;if(remote!=null)try{remote.send(Message.obtain(null,2));}catch(RemoteException ignored){}if(bound){context.unbindService(this);bound=false;}}
}
