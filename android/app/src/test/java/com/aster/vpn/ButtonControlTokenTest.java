package com.aster.vpn;
import org.junit.Test;
import static org.junit.Assert.*;
public class ButtonControlTokenTest {
 @Test public void tokensAreIndependentAndUrlSafe(){String first=ButtonControlToken.create(),second=ButtonControlToken.create();assertTrue(first.matches("[A-Za-z0-9_-]{43}"));assertFalse(first.equals(second));assertTrue(ButtonControlToken.matches(first,first));assertFalse(ButtonControlToken.matches(first,second));}
 @Test public void missingAndMalformedNeverAuthorize(){String token=ButtonControlToken.create();for(String bad:new String[]{null,"","short",token+"=",token+"&key="+token})assertFalse(ButtonControlToken.matches(token,bad));assertFalse(ButtonControlToken.matches(null,token));assertFalse(ButtonControlToken.matches("", ""));}
 @Test public void readsOnlySingleQueryCapability(){String token=ButtonControlToken.create();assertEquals(token,ButtonControlToken.fromLink("astervpn://toggle?key="+token));assertNull(ButtonControlToken.fromLink("astervpn://toggle"));assertNull(ButtonControlToken.fromLink(null));for(String bad:new String[]{"astervpn://toggle?key=a&key=b","astervpn://toggle?package=trusted","astervpn://toggle?key=a#fragment"})assertEquals("",ButtonControlToken.fromLink(bad));}
 @Test public void commandParsingKeepsCapabilityOutOfCommand(){String link="astervpn://disconnect?key="+ButtonControlToken.create();assertEquals("disconnect",LauncherCommand.parse(null,link));assertEquals("",LauncherCommand.parse(null,"https://toggle?key="+ButtonControlToken.create()));}
}
