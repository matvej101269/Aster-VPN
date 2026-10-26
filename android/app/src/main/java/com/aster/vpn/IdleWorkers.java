package com.aster.vpn;
import java.util.concurrent.*;
/** Serial work stays ordered, while idle UI/support threads release their stacks. */
final class IdleWorkers {
 static ThreadPoolExecutor single(String name){
  ThreadPoolExecutor worker=new ThreadPoolExecutor(1,1,15,TimeUnit.SECONDS,new LinkedBlockingQueue<>(),r->{Thread thread=new Thread(()->{android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_BACKGROUND);r.run();},name);return thread;});
  worker.allowCoreThreadTimeOut(true);return worker;
 }
}
