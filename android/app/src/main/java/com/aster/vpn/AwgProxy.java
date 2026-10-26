package com.aster.vpn;
/** Locally built AWG 3.1 netstack bridge. Authenticated loopback SOCKS only. */
public final class AwgProxy {
 static {System.loadLibrary("asterawg");}
 public static native int start(String config);
 public static native int socket(int version);
 public static native void stop();
}
