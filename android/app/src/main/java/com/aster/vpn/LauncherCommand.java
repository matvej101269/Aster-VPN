package com.aster.vpn;
import java.net.URI;
import java.util.Locale;
public final class LauncherCommand {
 public static String parse(String action,String link){
  String command="";if(action!=null&&action.startsWith("com.aster.vpn.action."))command=action.substring("com.aster.vpn.action.".length()).toLowerCase(Locale.ROOT);
  if(link!=null)try{URI uri=new URI(link);if(!"aster".equalsIgnoreCase(uri.getScheme())&&!"astervpn".equalsIgnoreCase(uri.getScheme()))return "";if(uri.getRawUserInfo()!=null||uri.getPort()!=-1)return "";command=uri.getHost();}catch(Exception e){return "";}
  if(command==null)return "";switch(command.toLowerCase(Locale.ROOT)){case "open":case "start":case "connect":return "connect";case "close":case "stop":case "disconnect":return "disconnect";case "toggle":return "toggle";case "status":return "status";default:return "";}
 }
 public static String operation(String command,boolean active){if(command.equals("toggle"))return active?"disconnect":"connect";return command;}
}
