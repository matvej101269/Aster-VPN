package com.aster.vpn;
/** Configure each Go runtime before the first native library is loaded. */
final class CoreRuntime {
 static void configure(){try{android.system.Os.setenv("GOMAXPROCS","2",true);android.system.Os.setenv("GOMEMLIMIT","96MiB",true);}catch(android.system.ErrnoException e){throw new IllegalStateException("Не удалось настроить сетевое ядро",e);}}
}
