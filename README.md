# Simple Port Forwarder (SPF)

[English](README.md) | [简体中文](README_zh.md) | [日本語](README_ja.md)

A lightweight, portable Windows utility to manage and run SSH port forwarding tunnels (Local, Remote, and Dynamic).

![Simple Port Forwarder GUI](docs/gui.png)

## Features

- **Port Forwarding**: Local (`-L`), Remote (`-R`), and Dynamic (`-D` SOCKS5 proxy) modes.
- **Security**: SSH passwords are entered at runtime and never saved to the configuration file.
- **Port Conflict Check**: Automatically detects if a port is in use before connecting.
- **Auto Reconnect**: Automatically reconnects if connection drops.
- **Logs Console**: Shows live status, connection details, and error logs.
- **Config Management**: Support for import/export configurations and cloning existing tunnels.

## Usage

Download pre-compiled binaries from the **GitHub Releases** page.

If you are compiling from source, the built executables will be generated in the `publish/` directory:
- `publish/SPF.exe` (Lightweight version, requires .NET 8.0 Desktop Runtime)
- `publish/self_contained/SPF.exe` (Self-contained version, runs standalone)

## Development

Requires .NET 8.0 SDK. Build the project using:
```bash
dotnet build SPF.sln
```
