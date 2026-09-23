const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;

const btnConnect = document.getElementById('btn-connect');
const btnDisconnect = document.getElementById('btn-disconnect');
const msgInput = document.getElementById('msg-input');
const messagesDiv = document.getElementById('messages');
const usersListDiv = document.getElementById('users-list');
const usersCountSpan = document.getElementById('users-count');
const statusBadge = document.getElementById('status-badge');

let isConnected = false;
let onlineUsers = new Set();

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
    const myUsername = document.getElementById('username').value.trim();
    msgInput.value = '';

    try {
      await invoke('send_message', { content: text });
      addChatMessage(myUsername, text, parseTimestamp(new Date()));
    } catch (err) {
      addSystemMessage('ОШИБКА', err);
    }
  }
});

listen('new-message', (event) => {
  const msg = event.payload;
  if (!msg) return;

  const sender = msg.sender || msg.Sender || '';
  const content = msg.content || msg.Content || '';
  const msgType = (msg.type || msg.Type || 'message').toLowerCase();
  const time = parseTimestamp(msg.timestamp || msg.Timestamp);

  // Ошибка от сервера
  if (msgType === 'error' || content.toLowerCase().includes('занят')) {
    addSystemMessage('ОШИБКА СЕРВЕРА', content);
    setConnectedState(false);
    return;
  }

  const isSystem = sender.toLowerCase() === 'system' || sender === 'СИСТЕМА' || sender === '' || msgType === 'system';

  // Обработка списка пользователей
  if (msgType === 'user_list' || msgType === 'users' || (isSystem && content.includes(','))) {
    onlineUsers.clear();
    content.split(',').forEach(u => {
      const trimmed = u.trim();
      if (trimmed) onlineUsers.add(trimmed);
    });
    updateUsersUI();
    if (msgType === 'user_list' || msgType === 'users') return;
  }

  // Обработка системных оповещений
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

  // Чат от любого пользователя
  if (sender) {
    if (!onlineUsers.has(sender)) {
      onlineUsers.add(sender);
      updateUsersUI();
    }
    addChatMessage(sender, content, time);
  }
});

listen('disconnected', (event) => {
  if (isConnected) {
    setConnectedState(false);
    addSystemMessage('СИСТЕМА', event.payload || 'Соединение с сервером потеряно.');
  }
});

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
}

function addChatMessage(author, text, timeStr) {
  const avatarLetter = author.charAt(0).toUpperCase();

  const div = document.createElement('div');
  div.className = 'msg-card';
  div.innerHTML = `
    <div class="msg-avatar">${avatarLetter}</div>
    <div class="msg-body">
      <div class="msg-header">
        <span class="msg-author">${escapeHtml(author)}</span>
        <span class="msg-time">${timeStr}</span>
      </div>
      <div class="msg-text">${escapeHtml(text)}</div>
    </div>
  `;
  messagesDiv.appendChild(div);
  messagesDiv.scrollTop = messagesDiv.scrollHeight;
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