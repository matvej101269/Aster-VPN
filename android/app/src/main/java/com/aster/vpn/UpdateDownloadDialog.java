package com.aster.vpn;
import android.app.*;
import android.widget.*;
import java.net.HttpURLConnection;
import java.util.Locale;

final class UpdateDownloadDialog {
 final Activity activity;
 private final AlertDialog dialog;
 private final ProgressBar bar;
 private final TextView status;
 private volatile boolean cancelled;
 private volatile HttpURLConnection connection;
 UpdateDownloadDialog(Activity activity,String version){
  this.activity=activity;int padding=Math.round(24*activity.getResources().getDisplayMetrics().density);
  LinearLayout content=new LinearLayout(activity);content.setOrientation(LinearLayout.VERTICAL);content.setPadding(padding,padding,padding,padding);
  bar=new ProgressBar(activity,null,android.R.attr.progressBarStyleHorizontal);bar.setMax(100);content.addView(bar,new LinearLayout.LayoutParams(-1,-2));
  status=new TextView(activity);status.setText("Подключение к серверу…");content.addView(status);
  dialog=new AlertDialog.Builder(activity).setTitle("Загрузка Aster VPN "+version).setView(content).setNegativeButton("Отмена",(d,w)->cancel()).create();
  dialog.setCanceledOnTouchOutside(false);dialog.setOnCancelListener(d->cancel());dialog.show();
 }
 boolean isCancelled(){return cancelled;}
 void connection(HttpURLConnection value){connection=value;if(cancelled)value.disconnect();}
 void cancel(){cancelled=true;HttpURLConnection c=connection;if(c!=null)c.disconnect();dialog.dismiss();}
 void report(long received,long total,boolean verifying){activity.runOnUiThread(()->{if(cancelled||activity.isDestroyed()||!dialog.isShowing())return;int percent=total>0?(int)Math.min(100,received*100/total):0;bar.setProgress(percent);status.setText(verifying?"Загрузка завершена. Проверка SHA-256 и подписи APK…":String.format(Locale.getDefault(),"%d%% · %.1f / %.1f МиБ",percent,received/1048576.0,total/1048576.0));});}
 void finish(){connection=null;dialog.dismiss();}
}
