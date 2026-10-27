package com.aster.vpn;
import java.util.*;
/** IPv4 TUN routes excluding local unicast, discovery multicast and broadcast. */
public final class LanRoutes {
 public static final String[] LOCAL={"10.0.0.0/8","172.16.0.0/12","192.168.0.0/16","169.254.0.0/16","224.0.0.0/4","255.255.255.255/32"};
 private static final List<String> PUBLIC=build();
 private static long ip(String text){long value=0;for(String part:text.split("\\."))value=(value<<8)|Integer.parseInt(part);return value;}
 private static List<String> build(){List<long[]> excluded=new ArrayList<>();for(String cidr:LOCAL){String[] p=cidr.split("/");long start=ip(p[0]);excluded.add(new long[]{start,start+(1L<<(32-Integer.parseInt(p[1])))-1});}List<String> routes=new ArrayList<>();split(0,0,excluded,routes);return Collections.unmodifiableList(routes);}
 private static void split(long start,int prefix,List<long[]> excluded,List<String> routes){long end=start+(1L<<(32-prefix))-1;boolean intersects=false;for(long[] range:excluded){if(start>=range[0]&&end<=range[1])return;if(start<=range[1]&&end>=range[0])intersects=true;}if(!intersects){routes.add(((start>>24)&255)+"."+((start>>16)&255)+"."+((start>>8)&255)+"."+(start&255)+"/"+prefix);return;}if(prefix<32){split(start,prefix+1,excluded,routes);split(start+(1L<<(31-prefix)),prefix+1,excluded,routes);}}
 public static List<String> tunnel(boolean bypass){return bypass?PUBLIC:Collections.singletonList("0.0.0.0/0");}
}
