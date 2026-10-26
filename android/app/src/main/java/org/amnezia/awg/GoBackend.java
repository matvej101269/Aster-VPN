package org.amnezia.awg;
/** JNI declarations matching the official AmneziaWG Android 3.1 backend. */
public final class GoBackend {
 static {System.loadLibrary("wg-go");}
 public static native String awgGetConfig(int handle);
 public static native int awgGetSocketV4(int handle);
 public static native int awgGetSocketV6(int handle);
 public static native void awgTurnOff(int handle);
 public static native int awgTurnOn(String name,int fd,String config);
 public static native String awgVersion();
}
