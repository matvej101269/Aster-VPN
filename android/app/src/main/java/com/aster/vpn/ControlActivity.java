package com.aster.vpn;
import android.app.Activity;
import android.content.*;
import android.os.Bundle;
import android.widget.Toast;
/** Visible user-triggered launcher entry point; VPN consent remains managed by Android. */
public final class ControlActivity extends Activity {
 @Override public void onCreate(Bundle state){super.onCreate(state);if(state!=null){finish();return;}String command=LauncherCommand.parse(getIntent().getAction(),getIntent().getDataString());try{
  if(command.isEmpty()){finish();return;}
  if(command.equals("status")){startActivity(new Intent(this,MainActivity.class));finish();return;}
  if(!new Store(this).read().optBoolean("externalControl",false)){Toast.makeText(this,"Включите «Управление из лаунчера» в настройках Aster VPN",Toast.LENGTH_LONG).show();startActivity(new Intent(this,MainActivity.class));finish();return;}
  String operation=LauncherCommand.operation(command,AsterVpnService.active||AsterVpnService.connecting);
  if(operation.equals("disconnect")){if(AsterVpnService.active||AsterVpnService.connecting)AsterVpnService.command(this,AsterVpnService.STOP);}
  else if(operation.equals("connect")&&!AsterVpnService.active&&!AsterVpnService.connecting)startActivity(new Intent(this,ConnectActivity.class).putExtra("last",true));
 }catch(Exception e){Toast.makeText(this,AsterVpnService.safeMessage(e),Toast.LENGTH_LONG).show();}finally{finish();}}
}
