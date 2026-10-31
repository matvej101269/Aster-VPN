package com.aster.vpn;
import org.junit.Test;
import static org.junit.Assert.*;
public class LauncherCallerTest {
 @Test public void androidPackageIsSufficient(){assertEquals("launcher",LauncherCaller.resolve("launcher",null));}
 @Test public void tvActivityFallback(){assertEquals("launcher",LauncherCaller.resolve(null,"launcher"));assertEquals("launcher",LauncherCaller.resolve("","launcher"));}
 @Test public void rejectsMissingOrConflictingIdentity(){assertNull(LauncherCaller.resolve(null,null));assertNull(LauncherCaller.resolve("", ""));assertNull(LauncherCaller.resolve("launcher","other"));}
 @Test public void agreesOnCaller(){assertEquals("launcher",LauncherCaller.resolve("launcher","launcher"));}
}
