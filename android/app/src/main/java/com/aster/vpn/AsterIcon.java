package com.aster.vpn;

import android.content.Context;
import android.graphics.*;
import android.view.View;

/** Small vector controls drawn at device density; no platform emoji or button chrome. */
public final class AsterIcon extends View {
 private final Path path=new Path();private final String kind; private int color; private final Paint paint=new Paint(3);
 public AsterIcon(Context c,String kind,int color){super(c);this.kind=kind;this.color=color;setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);}
 public void tint(int color){this.color=color;invalidate();}
 @Override protected void onDraw(Canvas canvas){super.onDraw(canvas);canvas.save();canvas.scale(getWidth()/24f,getHeight()/24f);paint.setColor(color);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(1.7f);paint.setStrokeCap(Paint.Cap.ROUND);paint.setStrokeJoin(Paint.Join.ROUND);
  Path p=path;p.reset();switch(kind){
   case "power":canvas.drawArc(5,5,19,19,-48,276,false,paint);canvas.drawLine(12,3,12,11,paint);break;
   case "home":p.moveTo(3,11);p.lineTo(12,3);p.lineTo(21,11);p.moveTo(5,10);p.lineTo(5,21);p.lineTo(10,21);p.lineTo(10,15);p.lineTo(14,15);p.lineTo(14,21);p.lineTo(19,21);p.lineTo(19,10);canvas.drawPath(p,paint);break;
   case "servers":canvas.drawRoundRect(3,4,21,10,2,2,paint);canvas.drawRoundRect(3,14,21,20,2,2,paint);canvas.drawPoint(7,7,paint);canvas.drawPoint(7,17,paint);canvas.drawLine(12,7,17,7,paint);canvas.drawLine(12,17,17,17,paint);break;
   case "routes":canvas.drawCircle(5,5,2,paint);canvas.drawCircle(19,19,2,paint);p.moveTo(5,7);p.lineTo(5,15);p.quadTo(5,19,9,19);p.lineTo(12,19);p.moveTo(19,17);p.lineTo(19,9);p.quadTo(19,5,15,5);p.lineTo(12,5);canvas.drawPath(p,paint);break;
   case "settings":canvas.drawCircle(12,12,4,paint);canvas.drawCircle(12,12,9,paint);for(int a=0;a<360;a+=45){double r=Math.toRadians(a);canvas.drawLine(12+(float)Math.cos(r)*9,12+(float)Math.sin(r)*9,12+(float)Math.cos(r)*11,12+(float)Math.sin(r)*11,paint);}break;
   case "plus":canvas.drawLine(12,5,12,19,paint);canvas.drawLine(5,12,19,12,paint);break;
   case "refresh":canvas.drawArc(4,4,20,20,40,285,false,paint);p.moveTo(16,3);p.lineTo(20,6);p.lineTo(20,1);canvas.drawPath(p,paint);break;
   case "ping":canvas.drawArc(3,4,21,22,180,180,false,paint);p.moveTo(4,18);p.lineTo(8,18);p.lineTo(11,11);p.lineTo(14,17);p.lineTo(20,8);canvas.drawPath(p,paint);break;
   case "search":canvas.drawCircle(10.5f,10.5f,6.5f,paint);canvas.drawLine(15.5f,15.5f,21,21,paint);break;
   case "chevron":p.moveTo(9,6);p.lineTo(15,12);p.lineTo(9,18);canvas.drawPath(p,paint);break;
   case "down":p.moveTo(6,9);p.lineTo(12,15);p.lineTo(18,9);canvas.drawPath(p,paint);break;
   case "check":p.moveTo(5,12);p.lineTo(10,17);p.lineTo(19,7);canvas.drawPath(p,paint);break;
   case "more":paint.setStrokeWidth(3);canvas.drawPoint(12,5,paint);canvas.drawPoint(12,12,paint);canvas.drawPoint(12,19,paint);break;
   case "shield":p.moveTo(12,2);p.lineTo(21,6);p.lineTo(20,14);p.quadTo(19,19,12,23);p.quadTo(5,19,4,14);p.lineTo(3,6);p.close();canvas.drawPath(p,paint);break;
   default:canvas.drawCircle(12,12,9,paint);canvas.drawOval(8,3,16,21,paint);canvas.drawLine(3,12,21,12,paint);break;
  }canvas.restore();
 }
}
