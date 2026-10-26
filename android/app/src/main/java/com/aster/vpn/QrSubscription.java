package com.aster.vpn;
import java.net.URI;
public final class QrSubscription {
 public static String address(String raw){if(raw==null||raw.trim().isEmpty())throw new IllegalArgumentException("QR-код пуст");String value=raw.trim();try{URI uri=new URI(value);if(!"https".equalsIgnoreCase(uri.getScheme())||uri.getHost()==null||uri.getHost().isEmpty()||uri.getRawUserInfo()!=null||uri.getPort()>65535||uri.getPort()==0)throw new Exception();return "https"+value.substring(value.indexOf(':'));}catch(Exception e){throw new IllegalArgumentException("QR-код должен содержать HTTPS-ссылку на подписку");}}
}
