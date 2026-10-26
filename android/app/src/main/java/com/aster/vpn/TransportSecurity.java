package com.aster.vpn;
import org.json.*;
/** Client-side TLS defaults; server keys and obfuscation parameters are never invented. */
public final class TransportSecurity {
 public static void harden(JSONObject outbound)throws JSONException{JSONObject stream=outbound.optJSONObject("streamSettings");if(stream==null)return;String security=stream.optString("security");if(!security.equals("tls")&&!security.equals("reality"))return;String key=security+"Settings";JSONObject settings=stream.optJSONObject(key);if(settings==null){settings=new JSONObject();stream.put(key,settings);}if(settings.optString("fingerprint").trim().isEmpty())settings.put("fingerprint","chrome");if(security.equals("tls"))settings.put("allowInsecure",false);}
}
