package com.aster.vpn;
/** Resolves only identity supplied by Android Activity APIs, never Intent metadata. */
final class LauncherCaller {
 static String resolve(String callingPackage,String activityPackage){
  String result=callingPackage==null||callingPackage.isEmpty()?activityPackage:callingPackage;
  if(result==null||result.isEmpty())return null;
  if(activityPackage!=null&&!result.equals(activityPackage))return null;
  return result;
 }
}
