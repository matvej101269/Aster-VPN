package com.aster.vpn;
import android.app.ActivityManager;
import android.content.Context;
import android.os.*;
import java.util.*;
public final class MemoryUsage {
 /** Total proportional resident memory, including native allocations in the AWG process. */
 public static long readKb(Context c){ActivityManager manager=c.getSystemService(ActivityManager.class);List<ActivityManager.RunningAppProcessInfo> processes=manager.getRunningAppProcesses();List<Integer> ids=new ArrayList<>();if(processes!=null)for(ActivityManager.RunningAppProcessInfo process:processes)if(process.uid==android.os.Process.myUid())ids.add(process.pid);if(ids.isEmpty())ids.add(android.os.Process.myPid());int[] pids=new int[ids.size()];for(int i=0;i<pids.length;i++)pids[i]=ids.get(i);long total=0;for(Debug.MemoryInfo info:manager.getProcessMemoryInfo(pids))if(info!=null)total+=info.getTotalPss();return total;}
}
