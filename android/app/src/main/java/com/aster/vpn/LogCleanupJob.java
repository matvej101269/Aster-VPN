package com.aster.vpn;
import android.app.job.*;
import android.content.*;
/** Offline cleanup; Android may defer background work while the device sleeps. */
public final class LogCleanupJob extends JobService {
 public static void schedule(Context c){JobScheduler scheduler=c.getSystemService(JobScheduler.class);if(scheduler.getPendingJob(21)==null)scheduler.schedule(new JobInfo.Builder(21,new ComponentName(c,LogCleanupJob.class)).setPeriodic(ConnectionLog.RETENTION_MS).build());}
 @Override public boolean onStartJob(JobParameters p){new Thread(()->{ConnectionLog.prune(this);jobFinished(p,false);},"Aster log cleanup").start();return true;}
 @Override public boolean onStopJob(JobParameters p){return true;}
}
