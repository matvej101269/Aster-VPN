package com.aster.vpn;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;

/** All subscriptions, keys and last connection stay in app-private encrypted storage. */
public final class Store {
 private static final Object LOCK = new Object();
 private final AtomicFile file;
 public Store(Context c) { file = new AtomicFile(new File(c.getFilesDir(), "vault.bin")); }
 private SecretKey key() throws Exception {
  KeyStore ks = KeyStore.getInstance("AndroidKeyStore"); ks.load(null);
  if (!ks.containsAlias("aster.vault.v1")) {
   KeyGenerator g = KeyGenerator.getInstance("AES", "AndroidKeyStore");
   g.init(new KeyGenParameterSpec.Builder("aster.vault.v1", KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build()); g.generateKey();
  }
  return (SecretKey)ks.getKey("aster.vault.v1", null);
 }
 public JSONObject read() throws Exception { synchronized(LOCK) {
  if (!file.getBaseFile().exists()) return new JSONObject().put("subscriptions", new JSONArray()).put("apps", new JSONArray()).put("mode", 0).put("autoUpdate", true);
  byte[] bytes = file.readFully(); if (bytes.length < 29 || bytes[0] != 1) throw new IOException("Повреждено хранилище Aster");
  Cipher c = Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, bytes, 1, 12));
  return new JSONObject(new String(c.doFinal(bytes,13,bytes.length-13), StandardCharsets.UTF_8));
 } }
 public void change(Edit edit) throws Exception { synchronized(LOCK) { JSONObject data=read(); edit.apply(data); save(data); } }
 public interface Edit { void apply(JSONObject data) throws Exception; }
 private void save(JSONObject data) throws Exception {
  Cipher c=Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.ENCRYPT_MODE,key()); byte[] encrypted=c.doFinal(data.toString().getBytes(StandardCharsets.UTF_8));
  FileOutputStream out=file.startWrite(); try { out.write(1); out.write(c.getIV()); out.write(encrypted); file.finishWrite(out); } catch(Exception e){file.failWrite(out);throw e;}
 }
 public static JSONObject find(JSONObject data,String id) {
  JSONArray subs=data.optJSONArray("subscriptions"); if(subs==null)return null;
  for(int i=0;i<subs.length();i++){JSONArray servers=subs.optJSONObject(i).optJSONArray("servers");if(servers==null)continue;for(int j=0;j<servers.length();j++){JSONObject p=servers.optJSONObject(j);if(id.equals(p.optString("id")))return p;}}
  return null;
 }
 public static JSONObject connection(JSONObject data, boolean last) {
  if(last && data.optJSONObject("lastConnection")!=null){
   JSONObject saved=data.optJSONObject("lastConnection");
   JSONObject current=find(data,saved.optString("id"));
   return current!=null?current:saved;
  }
  return find(data,data.optString("selected"));
 }
}

