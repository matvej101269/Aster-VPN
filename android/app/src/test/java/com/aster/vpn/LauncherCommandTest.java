package com.aster.vpn;
import org.junit.Test;
import static org.junit.Assert.*;
public class LauncherCommandTest {
 @Test public void deepLinksAndAliases(){assertEquals("toggle",LauncherCommand.parse(null,"astervpn://toggle"));assertEquals("connect",LauncherCommand.parse(null,"aster://open"));assertEquals("disconnect",LauncherCommand.parse(null,"astervpn://close"));assertEquals("status",LauncherCommand.parse(null,"astervpn://status"));}
 @Test public void explicitActions(){for(String s:new String[]{"CONNECT","DISCONNECT","TOGGLE"})assertEquals(s.toLowerCase(java.util.Locale.ROOT),LauncherCommand.parse("com.aster.vpn.action."+s,null));}
 @Test public void rejectsForeignAndMalformedCommands(){assertEquals("",LauncherCommand.parse(null,"happ://toggle"));assertEquals("",LauncherCommand.parse(null,"astervpn://user@toggle"));assertEquals("",LauncherCommand.parse("android.intent.action.MAIN",null));assertEquals("",LauncherCommand.parse(null,"astervpn://delete"));}
 @Test public void toggleIsStateDependentAndConnectIsIdempotent(){assertEquals("disconnect",LauncherCommand.operation("toggle",true));assertEquals("connect",LauncherCommand.operation("toggle",false));assertEquals("connect",LauncherCommand.operation("connect",true));assertEquals("connect",LauncherCommand.operation("connect",false));assertEquals("disconnect",LauncherCommand.operation("disconnect",true));assertEquals("disconnect",LauncherCommand.operation("disconnect",false));}
}
