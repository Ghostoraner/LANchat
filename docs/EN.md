# LANChat — Technical Documentation

> A self-hosted, encrypted local-network chat, implemented twice: once as a native C#/Avalonia desktop app, once as a Rust/Tauri web-style app — both speaking the same wire protocol against one shared server.

**Contents**

1. [Overview](#1-overview)
2. [Feature Set](#2-feature-set)
3. [Architecture](#3-architecture)
4. [Project Structure](#4-project-structure)
5. [Protocol Specification](#5-protocol-specification)
6. [Installation & Setup](#6-installation--setup)
7. [Running in Development](#7-running-in-development)
8. [Building Release Binaries](#8-building-release-binaries)
9. [Security Model](#9-security-model)
10. [Troubleshooting & Lessons Learned](#10-troubleshooting--lessons-learned)
11. [FAQ](#11-faq)
12. [Contributing](#12-contributing)
13. [License](#13-license)

---

## 1. Overview

LANChat is a chat application designed to run entirely inside a trusted local network — a home, an office, a LAN party — without any dependency on an external service, cloud account, or internet connection. One machine runs the server; every other machine on the same network finds it automatically through UDP broadcast discovery and connects over a TLS-encrypted TCP socket.

The defining trait of this project is that it ships **two independent, fully interoperable GUI clients**:

| | `LANChat.Client` | `lanchat-tauri` |
|---|---|---|
| Stack | C# / .NET 10 / Avalonia UI | Rust (Tauri 2) + HTML/CSS/JS |
| Look & feel | Native desktop widget toolkit | Discord-style web UI |
| Platforms | Windows, Linux | Windows, Linux, macOS |
| Status | Stable | Actively developed |

Both talk to the exact same `LANChat.Server` process using the exact same line-delimited JSON protocol over TLS, and can freely mix in the same chat room — a conversation between the two client implementations works exactly as if it were between two instances of the same one.

---

## 2. Feature Set

**Core networking**
- TCP server with per-connection isolation
- TLS 1.2/1.3 encryption with a dynamically generated self-signed certificate
- UDP-based LAN auto-discovery (broadcast on port 5001)
- Line-delimited JSON protocol, fully documented in [§5](#5-protocol-specification)
- Live online-user roster

**Messaging**
- Public chat messages and system notifications
- Message history: the server keeps the last 50 public messages in memory and replays them to a client right after it connects
- Private (direct) messages, addressed via a `to` field
- "Typing…" indicator, throttled on send and auto-expiring on receive
- File transfer: files are base64-encoded and streamed as chunked protocol lines, reassembled and saved to disk on arrival

**Hardening**
- Sender-spoofing protection: the server always overwrites the `sender` field before relaying anything
- Read/write timeouts on every connection (handshake, idle, write) to defeat slowloris-style resource exhaustion
- Socket keep-alive
- Maximum line length, bounding per-message memory use
- Rate limiting (500 ms minimum between regular messages; exempted for `typing`/`file_chunk` traffic)
- Per-socket error isolation during broadcast — one bad client can't take the rest down
- Username sanitization (length cap, commas stripped so they can't corrupt the comma-separated online-user list)

**Developer experience**
- `install.sh` (Linux/Termux) / `install.ps1` (Windows): detect the OS, install .NET SDK, Rust, Node.js, and Tauri's native build dependencies, then build everything
- `publish.sh` / `publish.ps1`: produce self-contained, single-file release binaries for the server and the classic client (no .NET runtime needed on the target machine)
- `npm run build` in `lanchat-tauri`: produces a native installer (`.msi`, `.AppImage`/`.deb`, `.dmg`)

---

## 3. Architecture

```
                     ┌──────────────────────────┐
                     │      LANChat.Server       │
                     │  TCP :5000  (TLS 1.2/1.3) │
                     │  UDP :5001  (discovery)   │
                     └─────────────┬──────────────┘
                     TLS + newline-delimited JSON
             ┌───────────────────────┼───────────────────────┐
             │                       │                        │
   ┌─────────▼──────────┐ ┌──────────▼─────────┐  ┌───────────▼─────────┐
   │  LANChat.Client     │ │   lanchat-tauri     │  │   any other client   │
   │  (C# / Avalonia)    │ │  (Rust / Tauri)     │  │   speaking the same  │
   │                      │ │                     │  │   protocol            │
   └──────────────────────┘ └─────────────────────┘  └───────────────────────┘
```

### Server components

- **`ChatServer`** — accepts TCP connections, generates a self-signed TLS certificate at startup, hands each socket off to a `ClientConnection`, and runs a UDP listener that answers discovery broadcasts.
- **`ClientConnection`** — owns the full lifecycle of one client: TLS handshake, nickname registration (and sanitization), the main read loop that decodes protocol lines, rate limiting, and all read/write timeouts.
- **`ClientManager`** — the thread-safe registry of connected clients keyed by username; the `Broadcast` primitive with per-socket error isolation; targeted delivery for private messages/files; and the bounded in-memory history buffer.

### Client components (both implementations mirror this shape)

- A **connection service** owning the TCP/TLS socket, the write path (serialized through a lock so concurrent writers — e.g. many file chunks — never interleave), and a background read loop that pushes parsed protocol messages upward.
- A **view layer** rendering the message list, the online-user roster, the typing indicator, and the recipient selector for private messages; it also owns reassembly of incoming `file_chunk` messages into a complete file.

---

## 4. Project Structure

```
LANChat/
├── LANChat.Server/              # Console TCP/TLS server
│   ├── ChatServer.cs            # Listener, cert generation, UDP discovery
│   ├── ClientConnection.cs      # Per-client protocol handling, timeouts, rate limiting
│   ├── ClientManager.cs         # Client registry, broadcast, history
│   └── Models/ChatMessage.cs
├── LANChat.Client/               # Classic GUI client (Avalonia MVVM)
│   ├── Services/ChatClient.cs
│   ├── ViewModels/
│   └── MainWindow.axaml
├── lanchat-tauri/                 # Discord-style client (Rust + Tauri)
│   ├── src/                       # Frontend (HTML/CSS/JS)
│   └── src-tauri/src/lib.rs       # Backend (Rust: TCP/TLS connection, file I/O)
├── install.sh / install.ps1       # Environment setup + build
├── publish.sh / publish.ps1       # Self-contained release builds
└── LANChat.slnx
```

---

## 5. Protocol Specification

Every protocol message is a single line of JSON terminated by `\n`, sent over a TLS-encrypted TCP stream. **The server is the single source of truth**: it always overwrites the `sender` field (and the top-level `timestamp`) on every message it relays, so a client can never make a message appear to come from someone else.

### 5.1 Common envelope

```json
{
  "type": "message",
  "sender": "Alice",
  "content": "Hello!",
  "timestamp": "2026-09-30T18:00:00Z",
  "to": null
}
```

| Field | Type | Meaning |
|---|---|---|
| `type` | string | One of the message types below |
| `sender` | string | Username; server-assigned on every relayed message |
| `content` | string | Payload — meaning depends on `type` |
| `timestamp` | ISO-8601 string | UTC, server-assigned on relay |
| `to` | string or `null` | Recipient username for private messages/files; empty/`null` = public |

### 5.2 Message types

| `type` | Direction | Purpose |
|---|---|---|
| `message` | client ↔ server ↔ all/one | A chat message, public or private |
| `typing` | client → server → everyone but sender | Typing indicator |
| `file_chunk` | client ↔ server ↔ all/one | One base64-encoded piece of a file transfer |
| `history` | server → new client | Snapshot of recent public messages |
| `users` | server → everyone | Comma-separated online-user list |
| `system` | server → everyone | Join/leave/connection notices |
| `error` | server → one client | Rejection (duplicate nickname, rate limit, etc.) |

**`message`** — public when `to` is empty/`null` (broadcast to everyone including the sender, and appended to server history); private when `to` is set (delivered only to that recipient, plus an echo back to the sender).

**`typing`** — clients should throttle sending (this project: once per 2 seconds) and auto-expire the indicator on the receiving side after ~3 seconds of silence.

**`file_chunk`** — files are too large for a single protocol line, so they're split client-side into base64 chunks:

| Extra field | Type | Meaning |
|---|---|---|
| `transferId` | string | Groups all chunks of one transfer (e.g. a UUID) |
| `fileName` | string | Original file name |
| `fileSize` | number | Total size in bytes |
| `chunkIndex` | number | 0-based index of this chunk |
| `totalChunks` | number | Total chunk count |
| `content` | string | Base64 payload for this chunk |

Public transfers (`to` empty) are broadcast to everyone **except** the sender — the sender already has the file and renders its own "sent" bubble locally from the original file data, not from a reconstructed copy. Private transfers go only to the named recipient.

**`history`** — sent once, right after a client registers. `content` is a **JSON-encoded array** of past public `message` entries (a string that must be parsed a second time). Only public messages are ever stored; private messages and files are never persisted, even transiently, beyond the moment of delivery.

**`users`** — `content` is a comma-separated username list, re-sent to everyone whenever the roster changes.

**`system`** — join/leave announcements and the initial connection confirmation.

**`error`** — the client must treat the connection attempt as failed on receiving this during the handshake (see [§10](#10-troubleshooting--lessons-learned) for a bug history around this exact point).

### 5.3 Connection sequence

```
Client                                        Server
  │ ── TLS handshake ─────────────────────────▶│
  │ ── {"sender":"Alice","content":""} ───────▶│   (handshake line — only `sender` matters)
  │                                             │   validates & sanitizes nickname
  │◀── {"type":"system","content":"Connected"} │   (or {"type":"error",...} + close, if taken)
  │◀── {"type":"users","content":"Alice,Bob"} ─│
  │◀── {"type":"system","content":"...joined"}─│
  │◀── {"type":"history","content":"[...]"} ───│
```

A correct client implementation **must** read and check this first response line for `type: "error"` before declaring the connection successful — skipping this check was a real bug found during this project's development (see §10.4).

---

## 6. Installation & Setup

### 6.1 Automated (recommended)

```bash
# Linux / Termux
chmod +x install.sh && ./install.sh
```
```powershell
# Windows
.\install.ps1
```

These scripts detect the OS/distribution; install the .NET SDK, Rust (via rustup), Node.js, and Tauri's native build dependencies (`webkit2gtk`, `gtk3`, `librsvg`, etc. on Linux); then build the server, the classic client, and the Tauri client.

### 6.2 Manual requirements

| Component | Requires |
|---|---|
| `LANChat.Server` | .NET 10 SDK |
| `LANChat.Client` | .NET 10 SDK |
| `lanchat-tauri` | Rust (stable) + Node.js + Tauri's native build dependencies |

---

## 7. Running in Development

```bash
# Server
dotnet run --project LANChat.Server

# Classic client (Avalonia)
dotnet run --project LANChat.Client

# Discord-style client (Tauri), with the dev window
cd lanchat-tauri
npm install
npm run dev
```

All three can run on the same machine for local testing — the server listens on `127.0.0.1:5000` by default, and both clients default to that address.

---

## 8. Building Release Binaries

### 8.1 Server + classic client — self-contained single file

```bash
./publish.sh             # both linux-x64 and win-x64
./publish.sh win-x64      # a single RID
```

Output lands in `release/<rid>/` and is zipped to `release/LANChat-<rid>.zip`. These binaries embed the .NET runtime — the recipient needs nothing installed. .NET's cross-publish support means you can build `win-x64` artifacts from a Linux machine (or vice versa) without any native Windows toolchain.

### 8.2 Tauri client — native installer

```bash
cd lanchat-tauri
npm run build
```

Produces, depending on the host OS: `.AppImage`/`.deb` (Linux), `.msi` (Windows), `.dmg` (macOS), under `src-tauri/target/release/bundle/`. Unlike .NET, Rust binaries do **not** cross-compile this easily — build on (or in CI for) each target platform separately.

---

## 9. Security Model

LANChat is explicitly designed for a **trusted local network**, not the open internet.

- **Transport encryption**: every connection uses TLS 1.2/1.3 with a self-signed certificate generated fresh at each server start, defeating passive eavesdropping on the LAN.
- **No certificate validation**: clients intentionally accept any server certificate. This is a deliberate trade-off — distributing a trusted CA to every participant on an ad-hoc LAN chat would be real deployment friction for little practical benefit. The consequence: an *active* man-in-the-middle already present on the LAN could impersonate the server. If that threat matters for your use case, this project is not sufficient as-is.
- **Sender spoofing is impossible**: the server always overwrites `sender` before relaying.
- **Denial-of-service mitigations**:
  - Rate limiting (500 ms between regular messages; `typing`/`file_chunk` exempted so they aren't throttled to uselessness)
  - Maximum protocol line length, bounding per-message memory
  - Read/write/handshake timeouts so an idle or unresponsive connection can't hold server resources forever
  - Per-socket error isolation in the broadcast loop
- **Username sanitization**: length capped, commas stripped (they would otherwise corrupt the comma-delimited online-user list).
- **Explicit non-goals**: authentication (anyone who can reach the TCP port and pick a free nickname can join), per-IP connection-count limiting, and protection against an active MITM already inside the LAN.

---

## 10. Troubleshooting & Lessons Learned

This section documents real issues found and fixed during this project's development — they're left in as a reference, since several are non-obvious platform quirks likely to resurface in similar projects.

### 10.1 Corrupted BOM breaking file parsers

**Symptom:** Avalonia fails to build with `File doesn't contain valid XAML: Data at the root level is invalid. Line 1, position 1`, or the C# compiler reports a cascade of nonsensical syntax errors starting at line 1 of an otherwise-correct `.cs` file.

**Cause:** a corrupted or duplicated byte-order-mark (BOM) at the very start of the file, typically introduced by copying file contents through certain editors, terminals, or clipboard managers.

**Fix:** strip everything before the first real content byte:
```bash
python3 -c "
content = open('MainWindow.axaml', 'rb').read()
idx = content.find(b'<Window')
with open('MainWindow.axaml', 'wb') as f:
    f.write(content[idx:])
"
```
When in doubt, recreate the file directly via a terminal heredoc (`cat > file << 'EOF' ... EOF`) to bypass clipboard/editor encoding entirely.

### 10.2 Tauri events silently never arriving

**Symptom:** the Tauri client connects successfully (`invoke()` calls succeed) but no message, system notice, or user-list update ever appears — the UI looks permanently empty.

**Cause:** Tauri 2's permission system (ACL) gates the core `event` plugin (`listen`/`emit`) separately from app-defined commands. App-defined `#[tauri::command]` functions are *not* subject to the ACL, which is why `invoke('connect_to_server', ...)` worked — but with no `capabilities/default.json` file at all, the event system had zero granted permissions, so every `emit()` on the Rust side was silently dropped.

**Fix:** add `src-tauri/capabilities/default.json`:
```json
{
  "identifier": "default",
  "windows": ["main"],
  "permissions": ["core:default", "core:event:default"]
}
```

### 10.3 Windows-only TLS handshake failure

**Symptom:** the server works fine between two Linux machines, but a Windows client gets `Received an unexpected EOF or 0 bytes from the transport stream` immediately after UDP discovery succeeds (i.e., discovery works, but the actual TLS/TCP connection fails).

**Cause:** `CertificateRequest.CreateSelfSigned(...)` returns a certificate backed by an **ephemeral** private key that lives only in memory. Windows' SChannel TLS stack cannot use such a key for a server-side handshake — it requires the key to be backed by a proper key container — while Linux's OpenSSL-based implementation tolerates it fine. The handshake fails on the server side, which closes the socket; the client sees a bare connection drop.

**Fix:** round-trip the certificate through a PFX export/import, which gives it a storage-backed (non-ephemeral) key:
```csharp
using var ephemeral = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));
return X509CertificateLoader.LoadPkcs12(
    ephemeral.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
```

### 10.4 Missing handshake-error check lets the client "succeed" on a dead connection

**Symptom:** a user connects with a nickname that's already taken. The server correctly rejects it and closes the socket — but the client's UI still shows "successfully connected," and any message the user sends afterward silently vanishes.

**Cause:** the C# client always read and checked the server's first response line for `type: "error"` before declaring success. The Rust/Tauri client's `connect_to_server` command did not — it declared success immediately after sending the handshake, without ever reading the server's reply.

**Fix:** read one line from the socket before returning success, and propagate it to the frontend as a failed connection attempt if its `type` is `"error"`; otherwise forward that same line to the UI as the first received message (it's usually the "connected" system notice) before starting the background read loop.

### 10.5 Reconnecting without aborting the old background task ("zombie connections")

**Symptom:** after reconnecting (or disconnecting and reconnecting) several times within the same running client process, incoming broadcasts — chat messages, and especially multi-chunk file transfers — start arriving duplicated, once per previous connection attempt.

**Cause:** the background task that reads from the server and emits `new-message` events was never explicitly stopped on disconnect or before establishing a new connection — only the *write* half of the socket was shut down. The read task kept running (and, since the server hadn't necessarily noticed the half-closed connection yet, kept being a live, registered participant), so every subsequent broadcast reached one listener per accumulated leftover connection.

**Fix:** store the task's `JoinHandle` and explicitly `.abort()` it both in `disconnect_server` and immediately before spawning a new one in `connect_to_server`.

### 10.6 Sparse-array trap breaking file reassembly

**Symptom:** incoming multi-chunk files from one client type arrive duplicated — specifically, once per chunk — and never open correctly (each "copy" contains only a fragment of the real data).

**Cause:** the chunk buffer was created with `new Array(totalChunks)`, which produces a *sparse* array: `totalChunks` unassigned slots, not `totalChunks` slots holding `undefined`. `Array.prototype.every()` silently skips unassigned slots in a sparse array rather than visiting them — so the "are all chunks present?" check (`chunks.every(c => typeof c === 'string')`) returned `true` as soon as just the *first* chunk had arrived, regardless of how many chunks the transfer actually had.

**Fix:** `new Array(totalChunks).fill(null)` — filling the array makes every slot a real value, so `.every()` correctly visits and checks all of them.

### 10.7 Blob-URL downloads silently failing inside the WebView

**Symptom:** after fixing §10.6, a fully and correctly reassembled file still can't be opened by clicking its link in the Tauri client.

**Cause:** `<a href="blob:..." download="...">` is a *browser* download mechanism, and Tauri's embedded WebView (WebKitGTK on Linux, WebView2 on Windows) does not reliably honor it for `blob:` URLs — clicks can silently do nothing or open a blank page, depending on platform and WebView version.

**Fix:** bypass the browser download mechanism entirely. Decode the base64 payload and write it to disk directly from Rust (`save_received_file` command), then open it with the OS's native file-open mechanism (`xdg-open` / `open` / `start`) via an `open_file` command, rather than relying on any webview-level download API.

### 10.8 Rust lifetimes breaking naive comment-stripping tools

**Symptom:** a generic "strip all comments" script, written to treat every `'` as the start of a string/char literal, corrupts `.rs` files — everything after the first lifetime annotation (`'a`, `'_`, `'static`, ...) gets swallowed as if it were inside an unterminated string.

**Cause:** Rust lifetimes use the same `'` character as char literals but, unlike a char literal, are never closed by a matching `'`. A tokenizer that doesn't special-case this will treat the lifetime's `'` as opening a string that (from its perspective) never ends.

**Fix:** before committing to "this `'` opens a string," look ahead a few characters for a plausible closing `'` (accounting for `\n`, `\\`, and `\u{...}`-style escapes); if none is found nearby, treat the `'` as an ordinary character instead of a string delimiter.

### 10.9 Tauri requires an RGBA icon

**Symptom:** `cargo tauri build`/`dev` fails with `proc macro panicked: icon ... is not RGBA`.

**Cause:** Tauri's build-time icon processing requires the app icon to carry an explicit alpha channel; a plain RGB (or palette-indexed) PNG is rejected outright, even if visually indistinguishable from an RGBA one.

**Fix:**
```bash
python3 -c "
from PIL import Image
Image.open('icon.png').convert('RGBA').save('icon.png')
"
```

---

## 11. FAQ

**Can this be used over the internet instead of just a LAN?**
Not recommended without changes. UDP discovery only works within one broadcast domain, and the "accept any certificate" trust model (§9) becomes a real risk outside a network you fully control.

**Why maintain two separate clients instead of one?**
They serve different goals — a native desktop feel versus a modern web-technology UI — and are kept as parallel, fully protocol-compatible implementations, which incidentally made the protocol itself much more rigorously specified (bugs that one implementation's quirks could paper over tend to surface quickly when a second, independent implementation has to interoperate with it).

**Where is chat history stored?**
Nowhere persistent. The server keeps the last 50 public messages in memory for newly-connecting clients; restarting the server clears it. Private messages and files are never stored, even transiently, beyond the moment of delivery.

---

## 12. Contributing

Pull requests and issues are welcome. Because two independent client implementations must remain wire-compatible, please open an issue to discuss any protocol change (new message types or fields) before submitting a PR, so both clients can be updated together.

## 13. License

Distributed under the MIT License — see `LICENSE`.