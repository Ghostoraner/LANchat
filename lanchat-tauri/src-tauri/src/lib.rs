use base64::Engine;
use native_tls::TlsConnector as NativeTlsConnector;
use serde_json::{json, Value};
use std::path::PathBuf;
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::{AppHandle, Emitter, State};
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader, WriteHalf};
use tokio::net::TcpStream;
use tokio::sync::Mutex;
use tokio::task::JoinHandle;
use tokio_native_tls::TlsConnector;

fn get_iso_timestamp() -> String {
    let now = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default();
    let secs = now.as_secs();
    let days = secs / 86400;
    let mut rem = secs % 86400;
    let hours = rem / 3600;
    rem %= 3600;
    let minutes = rem / 60;
    let seconds = rem % 60;
    let (year, month, day) = days_to_date(days);
    format!("{:04}-{:02}-{:02}T{:02}:{:02}:{:02}Z", year, month, day, hours, minutes, seconds)
}

fn days_to_date(days: u64) -> (u64, u64, u64) {
    let z = days + 719468;
    let era = z / 146097;
    let doe = z - era * 146097;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    (y, m, d)
}

type TlsWriteHalf = WriteHalf<tokio_native_tls::TlsStream<TcpStream>>;

pub struct AppState {
    pub writer: Arc<Mutex<Option<TlsWriteHalf>>>,
    pub username: Arc<Mutex<String>>,
    pub read_task: Arc<Mutex<Option<JoinHandle<()>>>>,
}

#[tauri::command]
async fn connect_to_server(
    app: AppHandle,
    state: State<'_, AppState>,
    ip: String,
    port: u16,
    username: String,
) -> Result<String, String> {
    {
        let mut writer_guard = state.writer.lock().await;
        if let Some(mut old_writer) = writer_guard.take() {
            let _ = old_writer.shutdown().await;
        }
    }
    {
       
        let mut task_guard = state.read_task.lock().await;
        if let Some(old_task) = task_guard.take() {
            old_task.abort();
        }
    }

    let addr = format!("{}:{}", ip, port);
    
    let tcp_stream = TcpStream::connect(&addr)
        .await
        .map_err(|e| format!("Ошибка TCP: {}", e))?;

    let native_connector = NativeTlsConnector::builder()
        .danger_accept_invalid_certs(true)
        .build()
        .map_err(|e| format!("Ошибка TLS: {}", e))?;

    let connector = TlsConnector::from(native_connector);

    let tls_stream = connector
        .connect("LANChatServer", tcp_stream)
        .await
        .map_err(|e| format!("TLS ошибка: {}", e))?;

    let (read_half, mut write_half) = tokio::io::split(tls_stream);

    let handshake = json!({
        "type": "message",
        "sender": username,
        "content": "",
        "timestamp": get_iso_timestamp()
    });
    
    let mut handshake_json = handshake.to_string();
    handshake_json.push('\n');

    write_half
        .write_all(handshake_json.as_bytes())
        .await
        .map_err(|e| format!("Ошибка отправки рукопожатия: {}", e))?;
    write_half.flush().await.map_err(|e| e.to_string())?;

    let mut reader = BufReader::new(read_half);
    let mut first_line = String::new();
    let bytes_read = reader
        .read_line(&mut first_line)
        .await
        .map_err(|e| format!("Ошибка чтения ответа сервера: {}", e))?;

    if bytes_read == 0 {
        return Err("Сервер закрыл соединение без ответа.".into());
    }

    let first_clean = first_line.trim();
    let first_json: Value = serde_json::from_str(first_clean)
        .map_err(|e| format!("Некорректный ответ сервера: {}", e))?;

   
    if first_json.get("type").and_then(|v| v.as_str()) == Some("error") {
        let reason = first_json
            .get("content")
            .and_then(|v| v.as_str())
            .unwrap_or("Сервер отклонил подключение.");
        return Err(reason.to_string());
    }

    {
        let mut name_guard = state.username.lock().await;
        *name_guard = username.clone();
    }
    {
        let mut writer_guard = state.writer.lock().await;
        *writer_guard = Some(write_half);
    }

    let app_handle = app.clone();
    let writer_ref = Arc::clone(&state.writer);

   
    let _ = app_handle.emit("new-message", first_json);

  
    let handle = tokio::spawn(async move {
        let mut reader = reader;
        let mut line = String::new();

        while let Ok(bytes) = reader.read_line(&mut line).await {
            if bytes == 0 {
                break;
            }

            let clean = line.trim();
            if !clean.is_empty() {
                if let Ok(json_val) = serde_json::from_str::<Value>(clean) {
                    let _ = app_handle.emit("new-message", json_val);
                }
            }
            line.clear();
        }

        {
            let mut writer_guard = writer_ref.lock().await;
            *writer_guard = None;
        }
        let _ = app_handle.emit("disconnected", "Соединение с сервером разорвано.");
    });

    {
        let mut task_guard = state.read_task.lock().await;
        *task_guard = Some(handle);
    }

    Ok(format!("Успешно подключено к {}", addr))
}

#[tauri::command]
async fn send_message(state: State<'_, AppState>, content: String) -> Result<(), String> {
    let mut writer_guard = state.writer.lock().await;
    let username = state.username.lock().await.clone();
    
    if let Some(ref mut writer) = *writer_guard {
        let msg = json!({
            "type": "message",
            "sender": username,
            "content": content,
            "timestamp": get_iso_timestamp()
        });
        let mut json_str = msg.to_string();
        json_str.push('\n');

        if let Err(e) = writer.write_all(json_str.as_bytes()).await {
            *writer_guard = None;
            return Err(format!("Ошибка отправки: {}", e));
        }
        let _ = writer.flush().await;
        Ok(())
    } else {
        Err("Отсутствует активное подключение к серверу.".into())
    }
}

#[tauri::command]
async fn send_json(state: State<'_, AppState>, payload: Value) -> Result<(), String> {
    let mut writer_guard = state.writer.lock().await;

    if let Some(ref mut writer) = *writer_guard {
        let mut json_str = payload.to_string();
        json_str.push('\n');

        if let Err(e) = writer.write_all(json_str.as_bytes()).await {
            *writer_guard = None;
            return Err(format!("Ошибка отправки: {}", e));
        }
        let _ = writer.flush().await;
        Ok(())
    } else {
        Err("Отсутствует активное подключение к серверу.".into())
    }
}

fn home_dir() -> Option<PathBuf> {
    std::env::var_os("HOME")
        .or_else(|| std::env::var_os("USERPROFILE"))
        .map(PathBuf::from)
}

fn unique_path(dir: &std::path::Path, file_name: &str) -> PathBuf {
    let candidate = dir.join(file_name);
    if !candidate.exists() {
        return candidate;
    }

    let path = std::path::Path::new(file_name);
    let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("file");
    let ext = path.extension().and_then(|s| s.to_str());

    let mut i = 1;
    loop {
        let name = match ext {
            Some(e) => format!("{} ({}).{}", stem, i, e),
            None => format!("{} ({})", stem, i),
        };
        let candidate = dir.join(name);
        if !candidate.exists() {
            return candidate;
        }
        i += 1;
    }
}


#[tauri::command]
async fn save_received_file(file_name: String, data_base64: String) -> Result<String, String> {
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(data_base64.as_bytes())
        .map_err(|e| format!("Некорректные данные файла: {}", e))?;

    let home = home_dir().ok_or("Не удалось определить домашнюю директорию.")?;
    let dir = home.join("LANChat").join("Received");
    std::fs::create_dir_all(&dir).map_err(|e| format!("Не удалось создать папку: {}", e))?;

    let safe_name = file_name.replace(['/', '\\'], "_");
    let path = unique_path(&dir, &safe_name);

    std::fs::write(&path, &bytes).map_err(|e| format!("Не удалось сохранить файл: {}", e))?;

    Ok(path.to_string_lossy().to_string())
}

#[tauri::command]
async fn open_file(path: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    let result = std::process::Command::new("cmd")
        .args(["/C", "start", "", &path])
        .spawn();

    #[cfg(target_os = "macos")]
    let result = std::process::Command::new("open").arg(&path).spawn();

    #[cfg(all(unix, not(target_os = "macos")))]
    let result = std::process::Command::new("xdg-open").arg(&path).spawn();

    result.map(|_| ()).map_err(|e| format!("Не удалось открыть файл: {}", e))
}

#[tauri::command]
async fn disconnect_server(state: State<'_, AppState>) -> Result<(), String> {
    let mut writer_guard = state.writer.lock().await;
    if let Some(mut writer) = writer_guard.take() {
        let _ = writer.shutdown().await;
    }
    drop(writer_guard);

    let mut task_guard = state.read_task.lock().await;
    if let Some(task) = task_guard.take() {
        task.abort();
    }
    Ok(())
}

pub fn run() {
    tauri::Builder::default()
        .manage(AppState {
            writer: Arc::new(Mutex::new(None)),
            username: Arc::new(Mutex::new(String::new())),
            read_task: Arc::new(Mutex::new(None)),
        })
        .invoke_handler(tauri::generate_handler![
            connect_to_server, 
            send_message, 
            send_json,
            save_received_file,
            open_file,
            disconnect_server
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}