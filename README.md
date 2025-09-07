# GHub Driver Wrapper

![Language](https://img.shields.io/badge/language-C%23-178600.svg)
![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)
![Framework](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)
![Status](https://img.shields.io/badge/status-experimental-orange.svg)

A C# wrapper around a custom mouse I/O device exposed via **NT native APIs** (`ntdll.dll`).  
This project demonstrates how to open a device handle and send raw input data (button, x, y, wheel) directly using **P/Invoke**.

---

## Features

- Opens a target device under `\??\ROOT#SYSTEM#000X#{1abc05c0-c378-41b9-9cef-df1aba82b015}`  
- Sends custom IOCTLs to simulate/update mouse state  
- Manages unmanaged memory safely (no leaks)  
- Implements `IDisposable` for handle cleanup  
- Includes retry logic if device handle is invalid 
