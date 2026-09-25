const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;

const btnConnect = document.getElementById('btn-connect');
const btnDisconnect = document.getElementById('btn-disconnect');
const msgInput = document.getElementById('msg-input');
const messagesDiv = document.getElementById('messages');
const usersListDiv = document.getElementById('users-list');
const usersCountSpan = document.getElementById('users-count');
const statusBadge = document.getElementById('status-badge');
const recipientSelect = document.getElementById('recipient-select');
const btnAttach = document.getElementById('btn-attach');
const fileInput = document.getElementById('file-input');
const typingIndicator = document.getElementById('typing-indicator');

let isConnected = false;
let myUsername = '';
let onlineUsers = new Set();


const typingUsers = new Map();
const TYPING_TIMEOUT_MS = 3000;
const TYPING_SEND_THROTTLE_MS = 2000;
let lastTypingSentAt = 0;


const incomingFiles = new Map();
const FILE_CHUNK_CHARS = 4000; 

btnConnect.addEventListener('click', async () => {
  if (isConnected) return;

  const ip = document.getElementById('ip').value.trim() || '127.0.0.1';
  const port = parseInt(document.getElementById('port').value.trim()) || 5000;
  const username = document.getElementById('username').value.trim();

  if (!username) {
    addSystemMessage('ОШИБКА', 'Укажите никнейм!');
    return;
  }

  btnConnect.disabled = true;
  btnConnect.innerText = 'Подключение...';

  try {
    const res = await invoke('connect_to_server', { ip, port, username });
    myUsername = username;
    setConnectedState(true);
    addSystemMessage('СИСТЕМА', res);
  } catch (err) {
    setConnectedState(false);
    addSystemMessage('ОШИБКА', err);
  }
});

btnDisconnect.addEventListener('click', async () => {
  try {
    await invoke('disconnect_server');
  } catch (e) {}
  setConnectedState(false);
  addSystemMessage('СИСТЕМА', 'Вы отключились от сервера.');
});

msgInput.addEventListener('keypress', async (e) => {
  if (e.key === 'Enter' && msgInput.value.trim() !== '') {
    if (!isConnected) return;

    const text = msgInput.value.trim();
    const to = recipientSelect.value || null;
    msgInput.value = '';

    try {
      await invoke('send_json', {
        payload: {
          type: 'message',
          sender: myUsername,
          content: text,
          timestamp: new Date().toISOString(),
          to: to,
        },
      });
      
      
    } catch (err) {
      addSystemMessage('ОШИБКА', err);
    }
  }
});


msgInput.addEventListener('input', () => {
  if (!isConnected) return;
  const now = Date.now();
  if (now - lastTypingSentAt < TYPING_SEND_THROTTLE_MS) return;
  lastTypingSentAt = now;

  invoke('send_json', {
    payload: {
      type: 'typing',
      sender: myUsername,
      content: '',
      timestamp: new Date().toISOString(),
    },
  }).catch(() => {});
});

btnAttach.addEventListener('click', () => {
  if (!isConnected) return;
  fileInput.click();
});

fileInput.addEventListener('change', async () => {
  const file = fileInput.files[0];
  fileInput.value = '';
  if (!file || !isConnected) return;
  await sendFile(file);
});

async function sendFile(file) {
  const to = recipientSelect.value || null;
  const transferId = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`;

  let base64;
  try {
    base64 = await fileToBase64(file);
  } catch (e) {
    addSystemMessage('ОШИБКА', 'Не удалось прочитать файл: ' + e);
    return;
  }

  const totalChunks = Math.max(1, Math.ceil(base64.length / FILE_CHUNK_CHARS));

  try {
    for (let i = 0; i < totalChunks; i++) {
      const chunk = base64.slice(i * FILE_CHUNK_CHARS, (i + 1) * FILE_CHUNK_CHARS);
      await invoke('send_json', {
        payload: {
          type: 'file_chunk',
          sender: myUsername,
          content: chunk,
          timestamp: new Date().toISOString(),
          to: to,
          transferId,
          fileName: file.name,
          fileSize: file.size,
          chunkIndex: i,
          totalChunks,
        },
      });
    }
  } catch (err) {
    addSystemMessage('ОШИБКА', 'Ошибка отправки файла: ' + err);
    return;
  }

  
  const url = URL.createObjectURL(file);
  addFileMessage(myUsername, file.name, file.size, url, !!to, to);
}

function fileToBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      
      const result = reader.result;
      const idx = result.indexOf(',');
      resolve(idx >= 0 ? result.slice(idx + 1) : result);
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

function handleFileChunk(msg) {
  const id = msg.transferId;
  if (!id) return;

  let entry = incomingFiles.get(id);
  if (!entry) {
    entry = {
      fileName: msg.fileName || 'файл',
      fileSize: msg.fileSize || 0,
      totalChunks: msg.totalChunks || 1,
      chunks: new Array(msg.totalChunks || 1),
      sender: msg.sender,
      to: msg.to,
    };
    incomingFiles.set(id, entry);
  }

  entry.chunks[msg.chunkIndex] = msg.content;

  const isComplete = entry.chunks.every((c) => typeof c === 'string');
  if (!isComplete) return;

  incomingFiles.delete(id);
  try {
    const base64 = entry.chunks.join('');
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    const blob = new Blob([bytes]);
    const url = URL.createObjectURL(blob);
    addFileMessage(entry.sender, entry.fileName, entry.fileSize, url, !!entry.to, entry.to);
  } catch (e) {
    addSystemMessage('ОШИБКА', `Не удалось собрать файл "${entry.fileName}": ${e}`);
  }
}

listen('new-message', (event) => {
  const msg = event.payload;
  if (!msg) return;

  const sender = msg.sender || msg.Sender || '';
  const content = msg.content || msg.Content || '';
  const msgType = (msg.type || msg.Type || 'message').toLowerCase();
  const to = msg.to || msg.To || '';
  const time = parseTimestamp(msg.timestamp || msg.Timestamp);

  
  if (msgType === 'error' || content.toLowerCase().includes('занят')) {
    addSystemMessage('ОШИБКА СЕРВЕРА', content);
    setConnectedState(false);
    return;
  }

  const isSystem = sender.toLowerCase() === 'system' || sender === 'СИСТЕМА' || sender === '' || msgType === 'system';

  
  if (msgType === 'history') {
    try {
      const items = JSON.parse(content);
      items.forEach((item) => {
        const itemSender = item.sender || item.Sender || '';
        const itemContent = item.content || item.Content || '';
        const itemTime = parseTimestamp(item.timestamp || item.Timestamp);
        if (itemSender) addChatMessage(itemSender, itemContent, itemTime, false, null);
      });
    } catch (e) {}
    return;
  }

  
  if (msgType === 'typing') {
    if (!sender || sender === myUsername) return;
    registerTyping(sender);
    return;
  }

  
  if (msgType === 'file_chunk') {
    handleFileChunk(msg);
    return;
  }

  
  if (msgType === 'user_list' || msgType === 'users' || (isSystem && content.includes(','))) {
    onlineUsers.clear();
    content.split(',').forEach(u => {
      const trimmed = u.trim();
      if (trimmed) onlineUsers.add(trimmed);
    });
    updateUsersUI();
    if (msgType === 'user_list' || msgType === 'users') return;
  }

  
  if (isSystem) {
    if (content.includes('присоединился') || content.includes('приєднався')) {
      const parts = content.trim().split(' ');
      if (parts[0]) onlineUsers.add(parts[0]);
      updateUsersUI();
    } else if (content.includes('покинул') || content.includes('вийшов')) {
      const parts = content.trim().split(' ');
      if (parts[0]) onlineUsers.delete(parts[0]);
      updateUsersUI();
    }

    if (content.trim().length > 0) {
      addSystemMessage('СИСТЕМА', content, time);
    }
    return;
  }

  
  if (sender) {
    if (!onlineUsers.has(sender)) {
      onlineUsers.add(sender);
      updateUsersUI();
    }
    clearTyping(sender);
    addChatMessage(sender, content, time, !!to, to);
  }
});

listen('disconnected', (event) => {
  if (isConnected) {
    setConnectedState(false);
    addSystemMessage('СИСТЕМА', event.payload || 'Соединение с сервером потеряно.');
  }
});

function registerTyping(user) {
  if (typingUsers.has(user)) clearTimeout(typingUsers.get(user));
  const timer = setTimeout(() => clearTyping(user), TYPING_TIMEOUT_MS);
  typingUsers.set(user, timer);
  updateTypingIndicator();
}

function clearTyping(user) {
  if (typingUsers.has(user)) {
    clearTimeout(typingUsers.get(user));
    typingUsers.delete(user);
    updateTypingIndicator();
  }
}

function updateTypingIndicator() {
  const names = Array.from(typingUsers.keys());
  if (names.length === 0) {
    typingIndicator.innerText = '';
  } else if (names.length === 1) {
    typingIndicator.innerText = `${names[0]} печатает...`;
  } else {
    typingIndicator.innerText = `${names.join(', ')} печатают...`;
  }
}

function parseTimestamp(ts) {
  if (!ts) return new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  try {
    const d = new Date(ts);
    if (isNaN(d.getTime())) return ts;
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  } catch (e) {
    return ts;
  }
}

function setConnectedState(connected) {
  isConnected = connected;
  btnConnect.style.display = connected ? 'none' : 'block';
  btnConnect.disabled = false;
  btnConnect.innerText = 'Подключиться';
  
  btnDisconnect.style.display = connected ? 'block' : 'none';
  msgInput.disabled = !connected;

  if (connected) {
    statusBadge.innerText = 'В сети';
    statusBadge.className = 'status-badge online';
  } else {
    statusBadge.innerText = 'Офлайн';
    statusBadge.className = 'status-badge offline';
    onlineUsers.clear();
    updateUsersUI();
    typingUsers.forEach((t) => clearTimeout(t));
    typingUsers.clear();
    updateTypingIndicator();
    incomingFiles.clear();
  }
}

function updateUsersUI() {
  usersListDiv.innerHTML = '';
  usersCountSpan.innerText = onlineUsers.size;

  onlineUsers.forEach(user => {
    const item = document.createElement('div');
    item.className = 'user-item';
    item.innerHTML = `
      <div class="status-dot"></div>
      <span class="user-name">${escapeHtml(user)}</span>
    `;
    usersListDiv.appendChild(item);
  });

  
  const prevValue = recipientSelect.value;
  recipientSelect.innerHTML = '<option value="">Всем (общий чат)</option>';
  onlineUsers.forEach(user => {
    if (user === myUsername) return;
    const opt = document.createElement('option');
    opt.value = user;
    opt.innerText = user;
    recipientSelect.appendChild(opt);
  });
  if (Array.from(recipientSelect.options).some(o => o.value === prevValue)) {
    recipientSelect.value = prevValue;
  }
}

function addChatMessage(author, text, timeStr, isPrivate, to) {
  const avatarLetter = author.charAt(0).toUpperCase();
  const authorLabel = isPrivate && author === myUsername ? `Вы → ${to}` : author;

  const div = document.createElement('div');
  div.className = 'msg-card' + (isPrivate ? ' private-msg' : '');
  div.innerHTML = `
    <div class="msg-avatar">${avatarLetter}</div>
    <div class="msg-body">
      <div class="msg-header">
        <span class="msg-author">${escapeHtml(authorLabel)}</span>
        <span class="msg-time">${timeStr}</span>
      </div>
      <div class="msg-text">${escapeHtml(text)}</div>
    </div>
  `;
  messagesDiv.appendChild(div);
  messagesDiv.scrollTop = messagesDiv.scrollHeight;
}

function addFileMessage(author, fileName, fileSize, url, isPrivate, to) {
  const avatarLetter = (author || '?').charAt(0).toUpperCase();
  const authorLabel = isPrivate && author === myUsername ? `Вы → ${to}` : author;
  const sizeLabel = formatFileSize(fileSize);
  const time = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

  const div = document.createElement('div');
  div.className = 'msg-card' + (isPrivate ? ' private-msg' : '');
  div.innerHTML = `
    <div class="msg-avatar">${avatarLetter}</div>
    <div class="msg-body">
      <div class="msg-header">
        <span class="msg-author">${escapeHtml(authorLabel)}</span>
        <span class="msg-time">${time}</span>
      </div>
      <div class="msg-text">📎 Файл</div>
      <a class="file-link" href="${url}" download="${escapeHtml(fileName)}">${escapeHtml(fileName)} (${sizeLabel})</a>
    </div>
  `;
  messagesDiv.appendChild(div);
  messagesDiv.scrollTop = messagesDiv.scrollHeight;
}

function formatFileSize(bytes) {
  if (!bytes || bytes < 1024) return `${bytes || 0} Б`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} КБ`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} МБ`;
}

function addSystemMessage(title, text, timeStr) {
  const time = timeStr || new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const isError = title.includes('ОШИБКА');

  const div = document.createElement('div');
  div.className = `msg-card system-msg ${isError ? 'error-msg' : ''}`;
  div.innerHTML = `
    <div class="msg-avatar">${isError ? '!' : 'S'}</div>
    <div class="msg-body">
      <div class="msg-header">
        <span class="msg-author">${title}</span>
        <span class="msg-time">${time}</span>
      </div>
      <div class="msg-text">${escapeHtml(text)}</div>
    </div>
  `;
  messagesDiv.appendChild(div);
  messagesDiv.scrollTop = messagesDiv.scrollHeight;
}

function escapeHtml(str) {
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}