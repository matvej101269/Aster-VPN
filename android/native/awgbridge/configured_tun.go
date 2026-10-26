package main
import (
 "sync"
 "github.com/amnezia-vpn/amneziawg-go/v3/tun"
)
// The upstream reader snapshots S4 before Read. Return one empty batch after
// configuration so it reloads that padding before reading any real payload.
// See upstream issue 168. This leaves the packet format and parameters intact.
type configuredTUN struct {tun.Device;ready chan struct{};releaseOnce sync.Once;first bool}
func configureTUN(device tun.Device)*configuredTUN{return &configuredTUN{Device:device,ready:make(chan struct{})}}
func(t *configuredTUN) release(){t.releaseOnce.Do(func(){close(t.ready)})}
func(t *configuredTUN) Read(bufs [][]byte,sizes []int,offset int)(int,error){if !t.first{<-t.ready;t.first=true;return 0,nil};return t.Device.Read(bufs,sizes,offset)}
func(t *configuredTUN) Close()error{t.release();return t.Device.Close()}
