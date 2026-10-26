package com.aster.vpn;
import java.util.Locale;
/** Core counters are reset by queryAllOutboundTrafficStats; count only the proxy outbound. */
public final class TrafficRates {
 public static long[] bytes(String payload){long up=0,down=0;if(payload!=null)for(String row:payload.split(";")){String[] parts=row.split(",",3);if(parts.length!=3||!parts[0].equals("proxy"))continue;try{long value=Long.parseLong(parts[2]);if(value<0)continue;if(parts[1].equals("uplink"))up=Math.addExact(up,value);else if(parts[1].equals("downlink"))down=Math.addExact(down,value);}catch(NumberFormatException|ArithmeticException ignored){}}return new long[]{up,down};}
 public static long perSecond(long bytes,long elapsedMs){return bytes<=0||elapsedMs<=0?0:(long)Math.min(Long.MAX_VALUE,bytes*(1000.0/elapsedMs));}
 public static String format(long bytes){double value=Math.max(0,bytes);String[] units={"Б/с","КиБ/с","МиБ/с","ГиБ/с"};int unit=0;while(value>=1024&&unit<units.length-1){value/=1024;unit++;}return String.format(Locale.ROOT,unit==0?"%.0f %s":"%.1f %s",value,units[unit]);}
 public static String label(long up,long down){return "↓ "+format(down)+"   ↑ "+format(up);}
}
