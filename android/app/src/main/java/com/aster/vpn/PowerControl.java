package com.aster.vpn;
import android.animation.*;
import android.content.Context;
import android.graphics.*;
import android.view.*;
import android.view.animation.*;

/** State transitions run on Android's animation clock and respect reduced motion. */
public final class PowerControl extends View {
 private final Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);private final Shader glow=new RadialGradient(0,0,110,new int[]{0x182c4e17,0x00121e14},new float[]{.65f,1f},Shader.TileMode.CLAMP);private boolean active,connecting;private float blend,phase,wave=1;private ValueAnimator stateAnimator,spinner,success;private final android.os.Handler handler=new android.os.Handler(android.os.Looper.getMainLooper());private long connectingAt;private boolean pendingActive;private final Runnable complete=()->finishVisual();
 public PowerControl(Context c){super(c);setContentDescription("Подключить VPN");setClickable(true);setFocusable(true);}
 public void state(boolean active,boolean connecting){setContentDescription(connecting?"Отменить подключение":active?"Отключить VPN":"Подключить VPN");if(connecting&&!this.connecting){connectingAt=android.os.SystemClock.elapsedRealtime();handler.removeCallbacks(complete);this.connecting=true;pendingActive=false;}else if(!connecting&&this.connecting){pendingActive=active;long remaining=650-(android.os.SystemClock.elapsedRealtime()-connectingAt);handler.removeCallbacks(complete);if(active&&remaining>0&&ValueAnimator.areAnimatorsEnabled()){handler.postDelayed(complete,remaining);updateSpinner();return;}this.connecting=false;}if(!this.connecting)transition(active);updateSpinner();invalidate();}
 private void finishVisual(){connecting=false;transition(pendingActive);updateSpinner();invalidate();}
 private void transition(boolean value){if(active==value)return;active=value;if(stateAnimator!=null)stateAnimator.cancel();if(!ValueAnimator.areAnimatorsEnabled()||!isAttachedToWindow()){blend=active?1:0;return;}stateAnimator=ValueAnimator.ofFloat(blend,active?1:0);stateAnimator.setDuration(480);stateAnimator.setInterpolator(new PathInterpolator(.2f,0,0,1));stateAnimator.addUpdateListener(a->{blend=(float)a.getAnimatedValue();invalidate();});stateAnimator.start();if(success!=null)success.cancel();if(active){success=ValueAnimator.ofFloat(0,1);success.setDuration(850);success.setInterpolator(new DecelerateInterpolator());success.addUpdateListener(a->{wave=(float)a.getAnimatedValue();invalidate();});success.start();}}
 private void updateSpinner(){boolean run=connecting&&isAttachedToWindow()&&getWindowVisibility()==VISIBLE&&isShown()&&ValueAnimator.areAnimatorsEnabled();if(run&&spinner==null){spinner=ValueAnimator.ofFloat(0f,360f);spinner.setDuration(1200);spinner.setRepeatCount(ValueAnimator.INFINITE);spinner.setInterpolator(new LinearInterpolator());spinner.addUpdateListener(a->{phase=(float)a.getAnimatedValue();invalidate();});spinner.start();}else if(!run&&spinner!=null){spinner.cancel();spinner=null;}}
 @Override protected void onAttachedToWindow(){super.onAttachedToWindow();updateSpinner();}
 @Override protected void onDetachedFromWindow(){handler.removeCallbacks(complete);if(success!=null)success.cancel();if(stateAnimator!=null)stateAnimator.cancel();if(spinner!=null){spinner.cancel();spinner=null;}animate().cancel();super.onDetachedFromWindow();}
 @Override protected void onWindowVisibilityChanged(int visibility){super.onWindowVisibilityChanged(visibility);updateSpinner();}
 @Override protected void onVisibilityChanged(View view,int visibility){super.onVisibilityChanged(view,visibility);updateSpinner();}
 @Override public boolean onTouchEvent(MotionEvent event){if(ValueAnimator.areAnimatorsEnabled()){if(event.getActionMasked()==MotionEvent.ACTION_DOWN)animate().scaleX(.95f).scaleY(.95f).setDuration(100).start();else if(event.getActionMasked()==MotionEvent.ACTION_UP||event.getActionMasked()==MotionEvent.ACTION_CANCEL)animate().scaleX(1f).scaleY(1f).setDuration(220).setInterpolator(new DecelerateInterpolator()).start();}return super.onTouchEvent(event);}
 @Override protected void onFocusChanged(boolean gain,int direction,android.graphics.Rect old){super.onFocusChanged(gain,direction,old);invalidate();}
 @Override public boolean performClick(){return super.performClick();}
 private int mix(int a,int b){return Color.argb((int)(Color.alpha(a)+(Color.alpha(b)-Color.alpha(a))*blend),(int)(Color.red(a)+(Color.red(b)-Color.red(a))*blend),(int)(Color.green(a)+(Color.green(b)-Color.green(a))*blend),(int)(Color.blue(a)+(Color.blue(b)-Color.blue(a))*blend));}
 @Override protected void onDraw(Canvas c){super.onDraw(c);float scale=Math.min(getWidth(),getHeight())/228f;c.save();c.translate(getWidth()/2f,getHeight()/2f);c.scale(scale,scale);
  p.setStyle(Paint.Style.FILL);p.setShader(glow);p.setAlpha((int)(blend*255));c.drawCircle(0,0,110,p);p.setShader(null);p.setAlpha(255);
  p.setColor(mix(0xff101d15,0xff213416));c.drawCircle(0,0,91,p);
  p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(8);p.setColor(mix(0xff1f3525,0xff627e31));c.drawCircle(0,0,91,p);
  p.setStrokeWidth(1.4f);p.setColor(mix(0xff354c2d,0xffb8d44c));c.drawCircle(0,0,96,p);if(isFocused()){p.setStrokeWidth(3);p.setColor(0xffeef2e8);c.drawCircle(0,0,111,p);p.setStrokeWidth(1.4f);}p.setColor(mix(0xff1b3021,0xff4a6129));c.drawCircle(0,0,86,p);
  p.setStrokeWidth(5.5f);p.setStrokeCap(Paint.Cap.ROUND);p.setColor(mix(0xff749783,0xffc2df51));c.drawArc(-26,-25,26,27,-48,276,false,p);c.drawLine(0,-34,0,-4,p);
  if(connecting){float pulse=(float)(.5+.5*Math.sin(Math.toRadians(phase)));p.setStyle(Paint.Style.FILL);p.setColor(Color.argb((int)(12+20*pulse),194,223,81));c.drawCircle(0,0,100+5*pulse,p);p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(3);p.setColor(0xffc2df51);c.drawArc(-103,-103,103,103,phase-90,70+35*pulse,false,p);p.setStrokeWidth(1.5f);p.setColor(0x668fab44);c.drawArc(-108,-108,108,108,-phase,100,false,p);}
  if(active&&wave<1){p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(2);p.setColor(Color.argb((int)(130*(1-wave)),194,223,81));c.drawCircle(0,0,96+16*wave,p);}c.restore();
 }
}

