package main

import (
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"net"
)

func readAddress(r io.Reader) (string, []byte, error) {
	var kind [1]byte
	if _, err := io.ReadFull(r, kind[:]); err != nil {
		return "", nil, err
	}
	header := []byte{kind[0]}
	size := 0
	switch kind[0] {
	case 1:
		size = 4
	case 4:
		size = 16
	case 3:
		var n [1]byte
		if _, err := io.ReadFull(r, n[:]); err != nil {
			return "", nil, err
		}
		size = int(n[0])
		header = append(header, n[0])
		if size == 0 {
			return "", nil, errors.New("empty domain")
		}
	default:
		return "", nil, errors.New("invalid address type")
	}
	buf := make([]byte, size+2)
	if _, err := io.ReadFull(r, buf); err != nil {
		return "", nil, err
	}
	header = append(header, buf...)
	host := string(buf[:size])
	if kind[0] != 3 {
		host = net.IP(buf[:size]).String()
	}
	port := binary.BigEndian.Uint16(buf[size:])
	if port == 0 {
		return "", nil, errors.New("zero port")
	}
	return net.JoinHostPort(host, fmt.Sprint(port)), header, nil
}
