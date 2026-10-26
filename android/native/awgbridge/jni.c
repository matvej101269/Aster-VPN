#include <jni.h>
#include "_cgo_export.h"
JNIEXPORT jint JNICALL Java_com_aster_vpn_AwgProxy_start(JNIEnv *env,jclass cls,jstring config){const char *raw=(*env)->GetStringUTFChars(env,config,0);if(!raw)return -1;int port=asterStart((char*)raw);(*env)->ReleaseStringUTFChars(env,config,raw);return port;}
JNIEXPORT void JNICALL Java_com_aster_vpn_AwgProxy_stop(JNIEnv *env,jclass cls){asterStop();}
JNIEXPORT jint JNICALL Java_com_aster_vpn_AwgProxy_socket(JNIEnv *env,jclass cls,jint version){return asterSocket(version);}
