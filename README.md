# AxoPrint

Свой сервис удалённой печати. Печатаешь с любого устройства на свой домашний/офисный
принтер через арендованный VPS. На стороне отправителя принтер выглядит как **обычный
принтер Windows** — без своего драйвера, через штатный *Microsoft IPP Class Driver*.

```
┌─────────────────┐   IPP / HTTPS   ┌──────────────────┐  long-poll / HTTPS  ┌────────────────────┐
│ ПК-отправитель  │ ──────────────► │   VPS  (relay)   │ ◄────────────────── │ ПК-хост (Windows)  │
│ Windows IPP     │   print-job     │ IPP-сервер +     │   забирает задание  │ agent (трей)       │
│ (неск. очередей)│                 │ очередь заданий  │ ──────────────────► │ → SumatraPDF       │
└─────────────────┘                 └──────────────────┘     статусы         │ → физ. принтеры    │
        ▲                                                                      └────────────────────┘
        │  setup-клиент добавляет очереди в Windows (Add-Printer)
   ┌────┴─────────┐
   │ AxoPrint.Setup │
   └──────────────┘
```

## Компоненты

| Проект | Где работает | Назначение |
|--------|--------------|-----------|
| **AxoPrint.Ipp** | библиотека | бинарный кодек IPP (RFC 8010/8011): атрибуты, 1setOf, коллекции |
| **AxoPrint.Shared** | библиотека | модели DTO relay ↔ agent ↔ setup |
| **AxoPrint.Relay** | VPS (Linux/Windows) | IPP-сервер + очередь заданий + REST API для агента |
| **AxoPrint.Agent** | ПК с принтером (Windows) | регистрирует принтеры, забирает задания, печатает через SumatraPDF; трей |
| **AxoPrint.Setup** | ПК-отправитель (Windows) | в один клик добавляет IPP-очереди как принтеры Windows |

## Сборка

```bash
dotnet build
dotnet test          # 9 тестов: кодек IPP + сквозной прогон релея
```

Требуется **.NET 10 SDK**.

---

## 1. Релей на VPS

### Конфигурация

Настройки читаются из `appsettings.json` или переменных окружения (двойное подчёркивание = `:`):

| Ключ | Env | Назначение |
|------|-----|-----------|
| `Axo:Token` | `AXO__TOKEN` | **общий секрет.** Сгенерируй длинный случайный: `openssl rand -hex 32` |
| `Axo:BaseUri` | `AXO__BASEURI` | публичный адрес, напр. `https://print.example.com` (для построения URL принтеров) |
| `Axo:DataDir` | `AXO__DATADIR` | где хранить очередь/документы (по умолчанию `<contentRoot>/data`) |

### Публикация и запуск

```bash
dotnet publish src/AxoPrint.Relay -c Release -o /opt/axoprint
```

Релей слушает обычный HTTP — **TLS терминируется обратным прокси** (Caddy/nginx).
Файлы для деплоя — в [`deploy/`](deploy/):

- [`deploy/axoprint-relay.service`](deploy/axoprint-relay.service) — systemd-юнит
- [`deploy/Caddyfile`](deploy/Caddyfile) — Caddy с автоматическим Let's Encrypt

```bash
# systemd
sudo cp deploy/axoprint-relay.service /etc/systemd/system/
sudoedit /etc/systemd/system/axoprint-relay.service   # впиши свой AXO__TOKEN и BASEURI
sudo systemctl enable --now axoprint-relay

# Caddy (ставит TLS-сертификат сам)
sudo cp deploy/Caddyfile /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

Проверка: открой `https://print.example.com/` — увидишь статус релея.

---

## 2. Агент на ПК с принтером (Windows)

```powershell
dotnet publish src/AxoPrint.Agent -c Release -r win-x64 --self-contained -o C:\AxoPrint\Agent
```

1. **Поставь SumatraPDF** (портативный, бесплатный): https://www.sumatrapdfreader.org/
   Положи `SumatraPDF.exe` рядом с агентом (`C:\AxoPrint\Agent\`) либо укажи путь в настройках.
2. Запусти `AxoPrint.Agent.exe`. В окне впиши:
   - **Relay URL**: `https://print.example.com`
   - **Token**: тот же секрет, что на релее
3. Нажми **Save & Reconnect**. Статус станет «● Online», в логе — `Registered N printer(s)`.
4. Окно закрывается в трей; печать идёт в фоне.

**Автозапуск:** ярлык в `shell:startup` или Task Scheduler (триггер «At log on»).

---

## 3. Клиент-отправитель (любой ПК Windows)

```powershell
dotnet publish src/AxoPrint.Setup -c Release -r win-x64 --self-contained -o C:\AxoPrint\Setup
```

1. Запусти `AxoPrint.Setup.exe` (**от администратора** — добавление принтера требует прав).
2. Впиши **Relay URL** и **Token**, нажми **Connect**.
3. Отметь нужные принтеры → **Add selected to Windows**.
4. Готово: принтеры появились в системе. Печатай в них из любого приложения как обычно —
   задание уйдёт на релей, агент его заберёт и напечатает.

---

## Как это печатается

1. Windows рендерит документ в **PDF** и шлёт по IPP на релей (штатный IPP Class Driver).
2. Релей кладёт задание в очередь (на диск) и отдаёт ждущему агенту.
3. Агент скачивает PDF и печатает через `SumatraPDF.exe -print-to "<принтер>" -silent`
   с учётом копий/дуплекса/цвета из задания.
4. Статус (Completed/Aborted) возвращается на релей.

Если агент офлайн — задания **копятся** в очереди и печатаются, когда он вернётся.

## Безопасность

- Весь внешний трафик — по **HTTPS** (TLS на прокси). IPP-эндпоинт и API защищены
  единым **токеном** (в пути для IPP, `Bearer` для API), сравнение constant-time.
- Токен — это и есть ключ доступа: храни его в секрете, при утечке смени на релее и в клиентах.
- Для максимальной приватности можно вообще не публиковать релей наружу, а связать
  устройства через **Tailscale** и указать в клиентах приватный адрес — архитектура не меняется.

## Ограничения / на будущее

- Один агент (один хост принтеров) на релей. Мультиагентность — задел в API есть.
- Формат заданий — PDF (то, что шлёт Windows IPP). PWG-raster и доп. форматы — задел в кодеке есть.
- Печать на хосте требует SumatraPDF; альтернативные бэкенды (PDFium/Ghostscript) подключаются точечно.
