package com.aster.vpn;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.*;
import android.net.VpnService;
import android.os.*;
import android.widget.Toast;

/** Android-authenticated callers, or explicit local consent for ordinary Activity launches. */
public final class ControlActivity extends Activity {
 private final Handler handler=new Handler(Looper.getMainLooper());
 private String caller,command,operation;
 private boolean waitingConsent,waitingState,dispatched;
 private long deadline;
 private final Runnable poll=new Runnable(){public void run(){
  if(!waitingState)return;
  boolean done=operation.equals("connect")?AsterVpnService.active:!AsterVpnService.active&&!AsterVpnService.connecting;
  if(done){complete(true,"completed","Команда выполнена");return;}
  if(SystemClock.elapsedRealtime()>=deadline){complete(false,"timeout","Истекло время ожидания VPN: "+AsterVpnService.state);return;}
  handler.postDelayed(this,200);
 }};
 @Override public void onCreate(Bundle saved){super.onCreate(saved);
  caller=getCallingPackage();ComponentName activity=getCallingActivity();
  if(caller!=null&&!caller.isEmpty()&&activity!=null&&!caller.equals(activity.getPackageName())){complete(false,"unknown_caller","Данные Android о вызывающем приложении не совпадают");return;}
  caller=LauncherCaller.resolve(caller,activity==null?null:activity.getPackageName());
  command=LauncherCommand.parse(getIntent().getAction(),getIntent().getDataString());
  if(command.isEmpty()){complete(false,"invalid_command","Неизвестная команда лаунчера");return;}
  if(saved!=null){waitingConsent=saved.getBoolean("consent");waitingState=saved.getBoolean("waiting");dispatched=saved.getBoolean("dispatched");operation=saved.getString("operation");deadline=saved.getLong("deadline");if(waitingState){handler.post(poll);return;}if(waitingConsent)return;}
  authorize();
 }
 private void authorize(){try{
  org.json.JSONObject data=new Store(this).read();
  String supplied=ButtonControlToken.fromLink(getIntent().getDataString());
  if(supplied!=null){
   if(!data.optBoolean("externalControl",false)||!ButtonControlToken.matches(getSharedPreferences("button-control",MODE_PRIVATE).getString("token",null),supplied)){complete(false,"invalid_token","Личная ссылка кнопки неверна или отозвана. Создайте новую в настройках Aster.");return;}
   ConnectionLog.add(this,"Кнопка пульта: доступ по личной ссылке подтверждён");execute();return;
  }
  if(caller==null){confirmSingleCommand();return;}
  if(data.optBoolean("externalControl",false)&&getSharedPreferences("launcher-grants",MODE_PRIVATE).getBoolean(caller,false)){execute();return;}
  new AlertDialog.Builder(this).setTitle("Управление VPN из лаунчера")
   .setMessage("Разрешить приложению "+caller+" подключать и отключать VPN? Команды используют последний выбранный сервер.")
   .setPositiveButton("Разрешить",(dialog,which)->{try{new Store(this).change(d->d.put("externalControl",true));if(!getSharedPreferences("launcher-grants",MODE_PRIVATE).edit().putBoolean(caller,true).commit())throw new IllegalStateException("Не удалось сохранить разрешение");execute();}catch(Exception e){fail(e);}})
   .setNegativeButton("Отказать",(dialog,which)->complete(false,"denied","Пользователь запретил управление VPN"))
   .setOnCancelListener(dialog->complete(false,"denied","Подтверждение управления отменено")).show();
 }catch(Exception e){fail(e);}}
 private void confirmSingleCommand(){
  String label=command.equals("connect")?"Подключить VPN":command.equals("disconnect")?"Отключить VPN":command.equals("toggle")?"Переключить VPN":"Показать состояние VPN";
  ConnectionLog.add(this,"Обычный запуск Activity: Android не сообщил caller; ожидается разовое подтверждение команды "+command);
  new AlertDialog.Builder(this).setTitle(label+"?")
   .setMessage("Android не сообщил, какое приложение отправило запрос. Разрешить только эту команду? Для Button Mapper без повторных подтверждений создайте личную ссылку в настройках Aster → Кнопка пульта.")
   .setPositiveButton("Выполнить",(dialog,which)->execute())
   .setNegativeButton("Отмена",(dialog,which)->complete(false,"denied","Разовая команда не разрешена"))
   .setOnCancelListener(dialog->complete(false,"denied","Подтверждение команды отменено")).show();
 }
 private void execute(){try{
  operation=LauncherCommand.operation(command,AsterVpnService.active||AsterVpnService.connecting);
  if(operation.equals("status")){complete(true,"status","Состояние VPN получено");return;}
  if(operation.equals("disconnect")){
   if(!AsterVpnService.active&&!AsterVpnService.connecting){complete(true,"already_disconnected","VPN уже отключён");return;}
   dispatch(AsterVpnService.STOP);return;
  }
  if(AsterVpnService.active){complete(true,"already_connected","VPN уже подключён");return;}
  if(AsterVpnService.connecting){waitForState();return;}
  if(Store.connection(new Store(this).read(),true)==null){complete(false,"no_profile","Сначала добавьте подписку и выберите сервер в Aster");return;}
  Intent consent=VpnService.prepare(this);
  if(consent!=null){waitingConsent=true;ConnectionLog.add(this,"Лаунчер "+caller+": ожидается разрешение Android VPN");startActivityForResult(consent,10);}else dispatch(AsterVpnService.LAST);
 }catch(Exception e){fail(e);}}
 private void dispatch(String action){if(dispatched)return;AsterVpnService.command(this,action);dispatched=true;waitForState();}
 private void waitForState(){waitingState=true;deadline=SystemClock.elapsedRealtime()+60000;ConnectionLog.add(this,"Лаунчер "+caller+": "+operation+", ожидание результата");handler.postDelayed(poll,200);}
 @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(request!=10)return;waitingConsent=false;if(result!=RESULT_OK){complete(false,"vpn_denied","Пользователь отклонил разрешение Android VPN");return;}execute();}
 @Override protected void onSaveInstanceState(Bundle out){super.onSaveInstanceState(out);out.putBoolean("consent",waitingConsent);out.putBoolean("waiting",waitingState);out.putBoolean("dispatched",dispatched);out.putString("operation",operation);out.putLong("deadline",deadline);}
 @Override protected void onDestroy(){handler.removeCallbacks(poll);super.onDestroy();}
 @Override public void onBackPressed(){complete(false,"cancelled","Ожидание команды закрыто; состояние VPN проверяйте в Aster");}
 private void fail(Exception e){complete(false,"error",AsterVpnService.safeMessage(e));}
 private void complete(boolean success,String result,String message){
  waitingState=false;handler.removeCallbacks(poll);
  ConnectionLog.add(this,"Лаунчер "+(caller==null?"неизвестен":caller)+": "+result+" — "+message);
  Intent response=new Intent().putExtra("command",command).putExtra("result",result).putExtra("message",message).putExtra("active",AsterVpnService.active).putExtra("connecting",AsterVpnService.connecting).putExtra("state",AsterVpnService.state);
  setResult(success?RESULT_OK:RESULT_CANCELED,response);if(!success)Toast.makeText(this,message,Toast.LENGTH_LONG).show();finish();
 }
}
