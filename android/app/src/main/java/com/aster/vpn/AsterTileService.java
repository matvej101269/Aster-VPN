package com.aster.vpn;
import android.app.PendingIntent;
import android.content.*;
import android.os.Build;
import android.service.quicksettings.*;
public final class AsterTileService extends TileService {
 public static void refresh(Context c){requestListeningState(c,new ComponentName(c,AsterTileService.class));}
 @Override public void onStartListening(){super.onStartListening();Tile tile=getQsTile();if(tile==null)return;tile.setLabel("Aster VPN");tile.setState(AsterVpnService.active||AsterVpnService.connecting?Tile.STATE_ACTIVE:Tile.STATE_INACTIVE);String subtitle="Выберите сервер в Aster";if(AsterVpnService.connecting)subtitle="Подключение…";else if(AsterVpnService.active)subtitle=AsterVpnService.serverName;else try{org.json.JSONObject p=Store.connection(new Store(this).read(),true);if(p!=null)subtitle=p.optString("name","Последний сервер");}catch(Exception ignored){}if(Build.VERSION.SDK_INT>=29)tile.setSubtitle(subtitle);tile.setContentDescription("Aster VPN · "+subtitle);tile.updateTile();}
 // PendingIntent overload exists only on API 34+; the legacy branch supports Android 8–13.
 @android.annotation.SuppressLint("StartActivityAndCollapseDeprecated")
 @Override public void onClick(){super.onClick();unlockAndRun(()->{if(AsterVpnService.active||AsterVpnService.connecting){AsterVpnService.command(this,AsterVpnService.STOP);return;}try{if(android.net.VpnService.prepare(this)==null&&Store.connection(new Store(this).read(),true)!=null){AsterVpnService.command(this,AsterVpnService.LAST);return;}}catch(Exception ignored){}Intent i=new Intent(this,ConnectActivity.class).putExtra("last",true).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);if(Build.VERSION.SDK_INT>=34)startActivityAndCollapse(PendingIntent.getActivity(this,5,i,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE));else startActivityAndCollapse(i);});}
}

