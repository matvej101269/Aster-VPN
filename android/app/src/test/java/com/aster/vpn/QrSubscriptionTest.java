package com.aster.vpn;
import com.google.zxing.*;
import com.google.zxing.common.*;
import com.google.zxing.qrcode.*;
import org.junit.Test;
import static org.junit.Assert.*;
public class QrSubscriptionTest {
 @Test public void acceptsTrimmedHttpsSubscription(){assertEquals("https://example.com/sub?token=test",QrSubscription.address("  HTTPS://example.com/sub?token=test\n"));}
 @Test(expected=IllegalArgumentException.class)public void rejectsPlainHttp(){QrSubscription.address("http://example.com/sub");}
 @Test(expected=IllegalArgumentException.class)public void rejectsCredentials(){QrSubscription.address("https://user:password@example.com/sub");}
 @Test(expected=IllegalArgumentException.class)public void rejectsUnrelatedQr(){QrSubscription.address("WIFI:S:example;T:WPA;P:test;;");}
 @Test(expected=IllegalArgumentException.class)public void rejectsEmptyResult(){QrSubscription.address(null);}
 @Test public void decodesRealQrAndPreservesSubscriptionToken()throws Exception{String url="https://example.com/subscription?token=A_b-123%2Bxyz&device=tv";BitMatrix qr=new QRCodeWriter().encode(url,BarcodeFormat.QR_CODE,320,320);int[] pixels=new int[320*320];for(int y=0;y<320;y++)for(int x=0;x<320;x++)pixels[y*320+x]=qr.get(x,y)?0xff000000:0xffffffff;Result decoded=new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(320,320,pixels))));assertEquals(url,QrSubscription.address(decoded.getText()));}
}
