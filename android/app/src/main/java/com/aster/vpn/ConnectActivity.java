package com.aster.vpn;
import android.app.Activity;
import android.content.Intent;
import android.net.VpnService;
import android.os.Bundle;
import android.widget.Toast;
public final class ConnectActivity extends Activity {
 @Override public void onCreate(Bundle b){super.onCreate(b);if(b!=null)return;try{boolean last=getIntent().getBooleanExtra("last",true);if(Store.connection(new Store(this).read(),last)==null){startActivity(new Intent(this,MainActivity.class));Toast.makeText(this,"Сначала добавьте подписку и выберите сервер",Toast.LENGTH_LONG).show();finish();return;}Intent consent=VpnService.prepare(this);if(consent!=null)startActivityForResult(consent,10);else start();}catch(Exception e){Toast.makeText(this,AsterVpnService.safeMessage(e),Toast.LENGTH_LONG).show();finish();}}
 private void start(){AsterVpnService.command(this,getIntent().getBooleanExtra("last",true)?AsterVpnService.LAST:AsterVpnService.START);finish();}
 @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(request==10&&result==RESULT_OK)start();else finish();}
}
