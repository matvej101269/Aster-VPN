package com.aster.vpn;
import android.content.Context;
import android.util.AtomicFile;
import java.io.*;
/** Geodata loader checks for a real file before consulting the asset reader. */
public final class GeoAssets {
 public static synchronized void ensure(Context context)throws IOException{File target=new File(context.getFilesDir(),"geoip.dat");if(target.isFile()&&target.length()==18534419L)return;AtomicFile file=new AtomicFile(target);FileOutputStream output=file.startWrite();try(InputStream input=context.getAssets().open("geoip.dat")){byte[] buffer=new byte[65536];int n;while((n=input.read(buffer))!=-1)output.write(buffer,0,n);file.finishWrite(output);}catch(IOException e){file.failWrite(output);throw e;}}
}
