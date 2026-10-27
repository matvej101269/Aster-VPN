package com.aster.vpn;
import org.junit.Test;
import static org.junit.Assert.*;
public class LanRoutesTest {
 private long ip(String value){long n=0;for(String part:value.split("\\."))n=(n<<8)|Integer.parseInt(part);return n;}
 private boolean captured(String address,boolean bypass){long target=ip(address);for(String cidr:LanRoutes.tunnel(bypass)){String[] parts=cidr.split("/");int prefix=Integer.parseInt(parts[1]);long mask=prefix==0?0:(0xffffffffL<<(32-prefix))&0xffffffffL;if((target&mask)==(ip(parts[0])&mask))return true;}return false;}
 @Test public void privateAndDiscoveryTrafficBypassTun(){for(String ip:new String[]{"10.2.3.4","172.16.1.1","172.31.255.255","192.168.1.1","169.254.1.2","224.0.0.251","239.255.255.250","255.255.255.255"})assertFalse(ip,captured(ip,true));}
 @Test public void publicBoundariesStillUseTun(){for(String ip:new String[]{"1.1.1.1","9.255.255.255","11.0.0.0","172.15.255.255","172.32.0.0","192.167.255.255","192.169.0.0","223.255.255.255","240.0.0.0","255.255.255.254"})assertTrue(ip,captured(ip,true));}
 @Test public void disablingBypassCapturesLocalAddresses(){assertEquals(1,LanRoutes.tunnel(false).size());assertTrue(captured("192.168.1.1",false));assertTrue(captured("224.0.0.251",false));}
}
