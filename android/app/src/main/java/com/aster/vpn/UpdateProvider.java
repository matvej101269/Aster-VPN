package com.aster.vpn;
import android.content.*;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;
public final class UpdateProvider extends ContentProvider {
 public boolean onCreate(){return true;}
 private File file(Uri uri)throws FileNotFoundException{if(!"/update.apk".equals(uri.getPath()))throw new FileNotFoundException();return new File(getContext().getCacheDir(),"update.apk");}
 public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException{if(!"r".equals(mode))throw new FileNotFoundException();return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.MODE_READ_ONLY);}
 public String getType(Uri uri){return "application/vnd.android.package-archive";}
 public Cursor query(Uri uri,String[] projection,String selection,String[] args,String sort){try{File f=file(uri);String[] cols=projection==null?new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE}:projection;MatrixCursor c=new MatrixCursor(cols);Object[] row=new Object[cols.length];for(int i=0;i<cols.length;i++)row[i]=OpenableColumns.DISPLAY_NAME.equals(cols[i])?f.getName():OpenableColumns.SIZE.equals(cols[i])?f.length():null;c.addRow(row);return c;}catch(IOException e){return null;}}
 public Uri insert(Uri uri,ContentValues v){throw new UnsupportedOperationException();}public int update(Uri uri,ContentValues v,String s,String[] a){throw new UnsupportedOperationException();}public int delete(Uri uri,String s,String[] a){throw new UnsupportedOperationException();}
}
