# LANChat — Documentation (English)

> A local-network chat with two interchangeable clients (C#/Avalonia and Rust/Tauri) talking to one shared TLS+JSON server.

**Contents**
1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Protocol Specification](#3-protocol-specification)
4. [Installation & Setup](#4-installation--setup)
5. [Building Release Binaries](#5-building-release-binaries)
6. [Security Model](#6-security-model)
7. [Troubleshooting](#7-troubleshooting)
8. [FAQ](#8-faq)
9. [Contributing](#9-contributing)

---

## 1. Overview

LANChat is a self-hosted chat application designed to run entirely inside a trusted local network (home, office, LAN party) with no dependency on any external service. One machine runs the server; every other machine on the same network finds it automatically via UDP broadcast discovery and connects over a TLS-encrypted TCP socket.

The project ships **two independent GUI clients** that speak the exact same wire protocol and are fully interoperable against a single server instance:

| | `LANChat.Client` | `lanchat-tauri` |
|---|---|---|
| Stack | C# / .NET 10 / Avalonia UI | Rust (Tauri 2) + HTML/CSS/JS |
| Look | Native desktop widget toolkit | Discord-style web UI |
| Platforms | Windows, Linux | Windows, Linux, macOS |

The server (`LANChat.Server`) is a plain console application with no external database — all state (connected users, recent message history) lives in memory for the lifetime of the process.

---

## 2. Architecture

```
                     ┌─────────────────────────┐
                     │      LANChat.Server      │
                     │  TCP :5000  (TLS 1.2/1.3)│
                     │  UDP :5001  (discovery)  │
                     └────────────┬─────────────┘
                     TLS + newline-delimited JSON
             ┌────────────────────┼────────────────────┐
             │                    │                     │
   ┌─────────▼─────────┐ ┌────────▼─────────┐  ┌────────▼─────────┐
   │  LANChat.Client    │ │  lanchat-tauri   │  │  any other client │
   │  (C# / Avalonia)   │ │ (Rust / Tauri)   │  │  speaking the     │
   │                    │ │                  │  │  same protocol    │
   └────────────────────┘ └──────────────────┘  └────────────────────┘
```

### Server components

- **`ChatServer`** — accepts incoming TCP connections, generates a self-signed TLS certificate at startup, hands each socket off to a `ClientConnection`, and runs a UDP listener that answers discovery broadcasts on port 5001.
- **`ClientConnection`** — owns one client's lifecycle: TLS handshake, nickname registration, the read loop that parses incoming protocol lines, rate limiting, and read/write timeouts.
- **`ClientManager`** — thread-safe registry of connected clients (keyed by username), the `Broadcast` primitive (with per-socket error isolation), targeted delivery for private messages/files, and a bounded in-memory history buffer (last 50 public messages).

### Client components (both implementations mirror this shape)

- A **connection service** that owns the TCP/TLS socket, the write path, and a background read loop that emits parsed protocol messages upward.
- A **view layer** that renders messages, the online-user list, the typing indicator, and turns incoming `file_chunk` messages into a downloadable/openable file once all chunks have arrived.

---

## 3. Protocol Specification

Every protocol message is a single line of JSON, terminated by `\n`, sent over the TLS-encrypted TCP stream. The server is the **single source of truth**: it always overwrites the `sender` field on every message it relays, so client-side sender spoofing is not possible.

### 3.1 Common envelope

```json
{
  "type": "message",
  "sender": "Alice",
  "content": "Hello!",
  "timestamp": "2026-09-24T18:00:00Z",
  "to": null
}
```

| Field | Type | Always present? | Meaning |
|---|---|---|---|
| `type` | string | yes | One of the message types below |
| `sender` | string | yes | Username (server-assigned, never trust client input) |
| `content` | string | yes | Payload — meaning depends on `type` |
| `timestamp` | ISO-8601 string | yes | UTC timestamp, server-assigned on relay |
| `to` | string or `null` | only for private messages/files | Recipient username; empty/`null` = public |

### 3.2 Message types

#### `message` — chat message

Public when `to` is empty/`null` (broadcast to everyone, including the sender, and appended to server history). Private when `to` is set (delivered only to that user, plus an echo back to the sender so their own UI can render it).

#### `typing` — typing indicator

Sent by a client while the user is composing text. Server rebroadcasts it to everyone **except** the sender. Clients should throttle sending (this project throttles to once every 2 seconds) and auto-expire the indicator on the receiving side after ~3 seconds of silence.

#### `file_chunk` — one piece of a file transfer

Files are split client-side into base64-encoded chunks and streamed as individual protocol lines, since the line-oriented protocol has a maximum line length (8192 bytes by default).

| Extra field | Type | Meaning |
|---|---|---|
| `transferId` | string | Unique ID grouping all chunks of one file transfer (e.g. a UUID) |
| `fileName` | string | Original file name |
| `fileSize` | number | Total file size in bytes |
| `chunkIndex` | number | 0-based index of this chunk |
| `totalChunks` | number | Total number of chunks in the transfer |
| `content` | string | Base64-encoded chunk payload |

Public file transfers (`to` empty) are broadcast to everyone **except** the sender (the sender already has the file locally and renders its own "sent" bubble client-side). Private file transfers are delivered only to the named recipient.

#### `history` — message history snapshot

Sent once, right after a client successfully registers. `content` is a JSON-encoded **array** of past public `message` entries (as a string — clients must `JSON.parse`/`Deserialize` it a second time). Only public messages are ever stored server-side; private messages and file transfers are never persisted.

#### `users` — online user list

`content` is a comma-separated list of currently connected usernames. Sent to everyone whenever the roster changes.

#### `system` — system notice

Join/leave announcements and the initial "connected" confirmation. Purely informational; not stored in history.

#### `error` — server-side rejection

Sent when the server refuses something (duplicate nickname, rate-limit exceeded, oversized line, etc.). The client should surface this to the user and, for connection-level errors, tear down the socket.

### 3.3 Sequence: connecting

```
Client                                   Server
  │  TLS handshake                          │
  │ ───────────────────────────────────────▶│
  │  {"sender":"Alice", "content":""}        │  (handshake line, only `sender` is read)
  │ ───────────────────────────────────────▶│
  │                                          │  validates/sanitizes nickname
  │◀─────────────────────────────────────── │  {"type":"system","content":"Connected..."}
  │◀─────────────────────────────────────── │  {"type":"users","content":"Alice,Bob"}
  │◀─────────────────────────────────────── │  {"type":"system","content":"Alice joined."}
  │◀─────────────────────────────────────── │  {"type":"history","content":"[...]"}
```

---

## 4. Installation & Setup

### 4.1 Automated (recommended)

```bash
# Linux / Termux
chmod +x install.sh && ./install.sh
```
```powershell
# Windows
.\install.ps1
```

These scripts detect your OS/distro, install .NET SDK, Rust (for the Tauri client), Node.js and the native libraries Tauri needs to compile (webkit2gtk, gtk3, librsvg, etc. on Linux), then build every project.

### 4.2 Manual, per component

```bash
# Server
dotnet run --project LANChat.Server

# Classic client (Avalonia)
dotnet run --project LANChat.Client

# Discord-style client (Tauri) — development mode with hot reload
cd lanchat-tauri
npm install
npm run dev
```

### 4.3 Requirements summary

| Component | Requires |
|---|---|
| `LANChat.Server` | .NET 10 SDK |
| `LANChat.Client` | .NET 10 SDK |
| `lanchat-tauri` | Rust (stable, via rustup) + Node.js + Tauri's native build dependencies |

---

## 5. Building Release Binaries

### 5.1 Server + classic client — self-contained single file

```bash
./publish.sh            # both linux-x64 and win-x64
./publish.sh win-x64     # a single RID
```

Output lands in `release/<rid>/` and is also zipped to `release/LANChat-<rid>.zip`. These binaries bundle the .NET runtime, so the recipient does **not** need the SDK installed.

### 5.2 Tauri client — native installer

```bash
cd lanchat-tauri
npm run build
```

Produces, depending on the OS you run it on: `.AppImage`/`.deb` (Linux), `.msi` (Windows), `.dmg` (macOS), under `src-tauri/target/release/bundle/`. Rust binaries do **not** cross-compile as easily as .NET — build on (or in CI for) each target OS separately.

---

## 6. Security Model

LANChat is explicitly designed for a **trusted local network**, not the open internet. Understanding the trade-offs matters more than any single control in isolation:

- **Transport encryption:** every connection is TLS 1.2/1.3 using a self-signed certificate generated fresh at each server start. This defeats *passive* eavesdropping on the LAN.
- **No certificate validation:** clients intentionally accept any server certificate (`danger_accept_invalid_certs` in Rust, an always-`true` validation callback in C#). This is a deliberate trade-off — full PKI would be overkill for a LAN chat and would add real deployment friction (distributing a trusted CA to every client). The consequence is that an *active* man-in-the-middle already present inside the LAN could impersonate the server. If you need protection against that, this project is not sufficient as-is.
- **Sender spoofing:** impossible. The server always overwrites the `sender` field before relaying any message; a malicious client cannot make its messages appear to come from someone else.
- **Denial of service mitigations in place:**
  - Per-connection **rate limiting** (500 ms minimum between regular messages; `typing`/`file_chunk` are exempt so file transfers and typing indicators aren't throttled to uselessness).
  - **Maximum line length** (8192 bytes) to bound memory use per read.
  - **Read/write timeouts** (handshake: 10 s, idle read: 2 minutes, write: 10 s) so a connection that never sends or never reads cannot hold server resources indefinitely (slowloris-style attacks).
  - **Per-socket error isolation** in the broadcast loop — one misbehaving/dead client cannot take down delivery to everyone else.
- **Username sanitization:** commas are stripped (they would otherwise corrupt the comma-separated online-user list) and length is capped.
- **Not covered:** authentication (anyone who can reach the TCP port and knows/guesses a free nickname can join), connection-count limiting per IP, and protection against an active MITM already inside the LAN. These are acceptable gaps for the target use case; treat them as explicit scope boundaries, not oversights.

---

## 7. Troubleshooting

**"File doesn't contain valid XAML" / "Data at the root level is invalid" on a `.axaml` file**
Almost always a corrupted byte-order-mark (BOM) at the very start of the file, introduced when copy-pasting through certain editors or terminals. Fix by stripping everything before the first `<`:
```bash
python3 -c "
content = open('MainWindow.axaml', 'rb').read()
idx = content.find(b'<Window')
with open('MainWindow.axaml', 'wb') as f:
    f.write(content[idx:])
"
```

**Cascading C# compiler errors starting from line 1 of a `.cs` file that look nonsensical**
Same root cause as above (encoding corruption), or the file's line count doesn't match what you expect — check with `wc -l` against the source you meant to paste. When in doubt, recreate the file directly via a `cat > file << 'EOF' ... EOF` heredoc in the terminal to bypass clipboard/editor encoding entirely.

**Tauri client connects successfully but never shows any message, user list stays at 0**
This means Tauri's event system (`listen`/`emit`, used for delivering incoming server messages to the frontend) has no permission to run. In Tauri 2, this requires a capabilities file at `src-tauri/capabilities/default.json` granting at least:
```json
{
  "identifier": "default",
  "windows": ["main"],
  "permissions": ["core:default", "core:event:default"]
}
```
If that file is missing entirely, no events reach the UI, even though direct command invocations (`invoke(...)`) still work fine (app-defined commands bypass the ACL system; core plugin APIs like events do not).

**Rust build fails with confusing errors after running an automated comment-stripping or text-processing script**
Rust lifetimes (`'a`, `'_`, `'static`) use a single quote but are *not* char literals. Any tool that naively treats every `'` as the start of a string will misparse the rest of the file. Use a tool that specifically distinguishes lifetimes from char literals (checks whether a closing `'` follows within a few characters).

---

## 8. FAQ

**Can I use this over the internet, not just a LAN?**
Not recommended without changes. UDP discovery only works within a broadcast domain, and the "accept any certificate" trust model becomes a real risk outside a network you fully control.

**Why two separate clients instead of one?**
They were built at different times with different goals (a native desktop feel vs. a modern web-technology UI) and kept as parallel, fully-compatible implementations of the same protocol — partly as a design exercise in protocol/implementation separation.

**Where is chat history stored?**
Nowhere persistent. The server keeps the last 50 public messages in memory and hands them to newly-connecting clients; restarting the server clears it. Private messages and files are never stored, even transiently, beyond the moment of delivery.

---

## 9. Contributing

Pull requests and issues are welcome. Because two independent clients must stay wire-compatible, please open an issue to discuss any change to the protocol (new message types, new fields) before submitting a PR, so both client implementations can be updated together.
