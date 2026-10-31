package com.aster.vpn;

import android.app.*;
import android.content.*;
import android.widget.*;

/** Local opt-in and revocation UI. The personal link is deliberately never logged. */
final class ButtonControlSetup {
 static void show(Activity activity,Runnable enabled){
  new AlertDialog.Builder(activity).setTitle("Кнопка пульта / Button Mapper")
   .setMessage("Создать личную ссылку для подключения и отключения VPN без повторных подтверждений? Любое приложение с этой ссылкой сможет управлять VPN. Передавайте её только своему Button Mapper. Новая ссылка отменит прежнюю; выключение управления из лаунчера также её отзовёт.")
   .setNegativeButton("Отмена",null).setPositiveButton("Создать ссылку",(dialog,which)->{
    try{
     String token=ButtonControlToken.create();
     if(!activity.getSharedPreferences("button-control",0).edit().putString("token",token).commit())throw new IllegalStateException("Не удалось сохранить ссылку");
     new Store(activity).change(data->data.put("externalControl",true));enabled.run();
     String link="astervpn://toggle?key="+token;
     TextView text=new TextView(activity);text.setText("В Button Mapper включите «Начать как занятие».\nПакет: com.aster.vpn\nКомпонент: com.aster.vpn.ControlActivity\nДанные (личная ссылка):\n\n"+link+"\n\nДействие и extras можно оставить пустыми. Для отдельных кнопок замените toggle на connect или disconnect. Не публикуйте ссылку.");text.setTextIsSelectable(true);text.setPadding(24,24,24,24);
     new AlertDialog.Builder(activity).setTitle("Личная ссылка для кнопки").setView(text)
      .setPositiveButton("Скопировать ссылку",(d,w)->{ClipData clip=ClipData.newPlainText("Личная ссылка Aster",link);if(android.os.Build.VERSION.SDK_INT>=33){android.os.PersistableBundle extras=new android.os.PersistableBundle();extras.putBoolean(ClipDescription.EXTRA_IS_SENSITIVE,true);clip.getDescription().setExtras(extras);}activity.getSystemService(ClipboardManager.class).setPrimaryClip(clip);Toast.makeText(activity,"Личная ссылка скопирована",Toast.LENGTH_SHORT).show();})
      .setNegativeButton("Закрыть",null).show();
    }catch(Exception e){Toast.makeText(activity,AsterVpnService.safeMessage(e),Toast.LENGTH_LONG).show();}
   }).show();
 }
}
