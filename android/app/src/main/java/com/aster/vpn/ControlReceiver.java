package com.aster.vpn;
import android.content.*;
import android.net.VpnService;
/** Broadcast interface for launcher buttons; requires opt-in and prior VPN consent. */
public final class ControlReceiver extends BroadcastReceiver {
 private static final java.util.concurrent.ExecutorService WORKER=IdleWorkers.single("aster-control");
 @Override public void onReceive(Context context,Intent intent){String command=LauncherCommand.parse(intent.getAction(),intent.getDataString());if(command.isEmpty()||command.equals("status"))return;PendingResult result=goAsync();Context app=context.getApplicationContext();WORKER.execute(()->{try{org.json.JSONObject data=new Store(app).read();if(!data.optBoolean("externalControl",false))return;String operation=LauncherCommand.operation(command,AsterVpnService.active||AsterVpnService.connecting);if(operation.equals("disconnect")){if(AsterVpnService.active||AsterVpnService.connecting)AsterVpnService.command(app,AsterVpnService.STOP);}else if(!AsterVpnService.active&&!AsterVpnService.connecting&&VpnService.prepare(app)==null&&Store.connection(data,true)!=null)AsterVpnService.command(app,AsterVpnService.LAST);}catch(Exception e){ConnectionLog.add(app,"Команда лаунчера не выполнена: "+e.getClass().getSimpleName());}finally{result.finish();}});}
}
