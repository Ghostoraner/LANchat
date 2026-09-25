use native_tls::TlsConnector as NativeTlsConnector;
use serde_json::{json, Value};
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::{AppHandle, Emitter, State};
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader, WriteHalf};
use tokio::net::TcpStream;
use tokio::sync::Mutex;
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

    
    tokio::spawn(async move {
        let mut reader = BufReader::new(read_half);
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

#[tauri::command]
async fn disconnect_server(state: State<'_, AppState>) -> Result<(), String> {
    let mut writer_guard = state.writer.lock().await;
    if let Some(mut writer) = writer_guard.take() {
        let _ = writer.shutdown().await;
    }
    Ok(())
}

pub fn run() {
    tauri::Builder::default()
        .manage(AppState {
            writer: Arc::new(Mutex::new(None)),
            username: Arc::new(Mutex::new(String::new())),
        })
        .invoke_handler(tauri::generate_handler![
            connect_to_server, 
            send_message, 
            send_json,
            disconnect_server
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}