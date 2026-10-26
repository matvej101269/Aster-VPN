package com.aster.vpn;
public final class ServerNames {
 public static String flag(String value){int[] cp=value.codePoints().toArray();for(int i=0;i+1<cp.length;i++)if(regional(cp[i])&&regional(cp[i+1]))return new String(cp,i,2);return "";}
 public static String display(String value){StringBuilder result=new StringBuilder();int[] cp=value.codePoints().toArray();for(int i=0;i<cp.length;i++){if(i+1<cp.length&&regional(cp[i])&&regional(cp[i+1])){i++;if(i+1<cp.length&&cp[i+1]==0xfe0f)i++;continue;}result.appendCodePoint(cp[i]);}return result.toString().trim().replaceAll("^[\\s\\uFE0F\\u200D]+","");}
 private static boolean regional(int cp){return cp>=0x1f1e6&&cp<=0x1f1ff;}
}
