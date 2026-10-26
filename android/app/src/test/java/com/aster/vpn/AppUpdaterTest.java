package com.aster.vpn;
import org.json.*;
import org.junit.Test;
import static org.junit.Assert.*;
public class AppUpdaterTest {
 @Test public void everyStartupChecksEvenAfterRecentCheck(){assertTrue(AppUpdater.shouldCheck(false,true,1000,999));}
 @Test public void manualCheckBypassesInterval(){assertTrue(AppUpdater.shouldCheck(true,false,1000,999));}
 @Test public void backgroundCheckKeepsSixHourInterval(){assertFalse(AppUpdater.shouldCheck(false,false,1000,999));assertTrue(AppUpdater.shouldCheck(false,false,21600000,0));}
 private JSONObject release(String version)throws Exception {return new JSONObject().put("assets",new JSONArray().put(new JSONObject().put("name","AsterVPN-"+version+"-android.apk").put("browser_download_url","https://github.com/matvej101269/Aster-VPN/releases/download/test/app.apk").put("digest","sha256:"+"a".repeat(64)).put("size",123)));}
 @Test public void comparesNumerically(){assertTrue(AppUpdater.compare("1.0.100","1.0.99")>0);assertEquals(0,AppUpdater.compare("1.0","1.0.0"));}
 @Test public void findsHighestPlatformVersionAcrossReleases()throws Exception {assertEquals("9.0.10",AppUpdater.select(new JSONArray().put(release("9.0.2")).put(release("9.0.10"))).version);}
 @Test public void rejectsPrereleaseAndOlderVersions()throws Exception {assertNull(AppUpdater.select(new JSONArray().put(release("9.0.0").put("prerelease",true)).put(release("1.0.0"))));}
 @Test public void rejectsMissingDigestAndForeignDownload()throws Exception {JSONObject missing=release("9.0.0"),foreign=release("9.0.1");missing.getJSONArray("assets").getJSONObject(0).remove("digest");foreign.getJSONArray("assets").getJSONObject(0).put("browser_download_url","https://example.org/app.apk");assertNull(AppUpdater.select(new JSONArray().put(missing).put(foreign)));}
 @Test public void ignoresWindowsAssets()throws Exception {JSONObject r=release("9.0.0");r.getJSONArray("assets").getJSONObject(0).put("name","AsterVPN-Setup-9.0.0-x64.exe");assertNull(AppUpdater.select(new JSONArray().put(r)));}
}
