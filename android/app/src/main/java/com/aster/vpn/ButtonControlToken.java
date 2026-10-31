package com.aster.vpn;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.Base64;

/** Bearer capability created only after local user consent; never a caller identity. */
final class ButtonControlToken {
 static String create(){byte[] bytes=new byte[32];new SecureRandom().nextBytes(bytes);return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);}
 static String fromLink(String link){
  if(link==null)return null;
  try{URI uri=new URI(link);String query=uri.getRawQuery();
   if(query==null)return null;
   if(!query.startsWith("key=")||query.indexOf('&')>=0||uri.getRawFragment()!=null)return "";
   return query.substring(4);
  }catch(Exception e){return "";}
 }
 static boolean matches(String expected,String supplied){
  return expected!=null&&supplied!=null&&expected.matches("[A-Za-z0-9_-]{43}")&&supplied.matches("[A-Za-z0-9_-]{43}")
   &&MessageDigest.isEqual(expected.getBytes(StandardCharsets.US_ASCII),supplied.getBytes(StandardCharsets.US_ASCII));
 }
}
