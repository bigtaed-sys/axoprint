# AxoPrint

Свой сервис удалённой печати. Печатаешь с любого устройства на свой домашний/офисный
принтер через арендованный VPS. На стороне отправителя это **обычный принтер Windows**
на встроенном (подписанном) драйвере *Microsoft Print to PDF* — своего драйвера писать
не надо.

```
┌──────────────────────────┐   HTTPS   ┌──────────────────┐  long-poll/HTTPS ┌────────────────────┐
│ ПК-отправитель           │ ────────► │   VPS  (relay)   │ ◄─────────────── │ ПК-хост (Windows)  │
│ принтер "AxoPrint: …"    │  PDF job  │ очередь заданий  │  забирает задание │ agent (трей)       │
│ = MS Print to PDF → файл │           │ + REST API       │ ───────────────► │ → SumatraPDF       │
│ клиент-трей шлёт PDF ────┘           └──────────────────┘     статусы       │ → физ. принтеры    │
└──────────────────────────┘                                                  └────────────────────┘
```

**Почему так, а не «нативный IPP-принтер на VPS»:** современная Windows не даёт чисто
добавить driverless-IPP-принтер по интернет-URL (driverless работает только для локально
обнаруженных устройств, а legacy internet-printing шлёт RAW и режется политикой печати).
Поэтому на отправителе принтер **локальный** (печатает в PDF-файл), а маленький клиент-трей
форвардит этот PDF на VPS. В Windows это всё равно обычный принтер, печать — чистый PDF.

## Компоненты

| Проект | Где работает | Назначение |
|--------|--------------|-----------|
| **AxoPrint.Shared** | библиотека | модели DTO relay ↔ agent ↔ client |
| **AxoPrint.Relay** | VPS (Linux/Windows) | очередь заданий + REST API (приём PDF) + IPP-сервер (для CUPS/Linux) |
| **AxoPrint.Agent** | ПК с принтером (Windows) | регистрирует принтеры, забирает задания, печатает через SumatraPDF; трей |
| **AxoPrint.Setup** | ПК-отправитель (Windows) | создаёт локальные PDF-принтеры и форвардит их вывод на VPS; трей |
| **AxoPrint.LinuxClient** | ПК-отправитель (Linux) | добавляет в CUPS IPP-принтеры, смотрящие на VPS (AppImage) |
| **AxoPrint.Ipp** | библиотека | кодек IPP (RFC 8010/8011) — использует IPP-сервер релея для Linux/CUPS |

## Сборка

```bash
dotnet build
dotnet test          # сквозные прогоны релея (REST API + очередь)
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

### Установка одной командой (Debian/Ubuntu или AlmaLinux/RHEL)

Скрипт сам определяет дистрибутив (`apt` или `dnf`), настраивает `ufw`/`firewalld`
и SELinux. Нужен домен с DNS-записью на сервер и открытые порты 80/443. На сервере:

```bash
git clone https://github.com/bigtaed-sys/axoprint.git
cd axoprint
sudo bash deploy/install.sh print.example.com
```

Скрипт [`deploy/install.sh`](deploy/install.sh) сам:
1. ставит **.NET 10 SDK** (если нет) в `/opt/dotnet`;
2. собирает релей как **self-contained** бинарь в `/opt/axoprint`;
3. создаёт systemd-сервис `axoprint-relay` (слушает `127.0.0.1`, `Type=notify`, логи в journald);
4. ставит **Caddy** и настраивает TLS-реверс-прокси для домена (сертификат Let's Encrypt автоматом);
5. запускает всё и печатает **URL и токен** — этот токен впишешь в Agent и Setup.

Токен можно задать вторым аргументом (`... install.sh print.example.com МОЙ_ТОКЕН`); иначе сгенерируется.

Проверка: `curl https://print.example.com/` (дай минуту на выпуск сертификата).
Логи: `journalctl -u axoprint-relay -f`.

> Эталонные файлы для ручной установки или Windows-VPS — в [`deploy/`](deploy/)
> ([`axoprint-relay.service`](deploy/axoprint-relay.service), [`Caddyfile`](deploy/Caddyfile)).

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
dotnet publish src/AxoPrint.Setup -c Release -r win-x64 --self-contained -o C:\AxoPrint\Client
```

1. Запусти `AxoPrint.Setup.exe` (**от администратора** — создание принтера требует прав).
2. Впиши **Relay URL** и **Token**, нажми **Connect**.
3. Отметь нужные принтеры → **Add selected to Windows**. Появятся принтеры
   `AxoPrint: <имя>` (на драйвере *Microsoft Print to PDF*).
4. Печатай в них из любого приложения. **Клиент должен оставаться запущенным** (в трее) —
   он подхватывает PDF и шлёт на релей. Окно закрывается в трей.

**Автозапуск:** ярлык в `shell:startup` или Task Scheduler («At log on», с правами админа).

---

## 4. Клиент-отправитель на Linux (CUPS, AppImage)

В отличие от Windows, CUPS добавляет IPP-принтер по интернет-URL штатно и без драйверов,
поэтому на Linux отдельный аплоадер не нужен — CUPS печатает прямо на релей по IPP.

Собрать AppImage (на любой Linux-машине с .NET 10 SDK):
```bash
git clone https://github.com/bigtaed-sys/axoprint.git
cd axoprint
bash deploy/appimage/build-appimage.sh      # → build/AxoPrint-x86_64.AppImage
```

Запуск:
```bash
chmod +x build/AxoPrint-x86_64.AppImage
./build/AxoPrint-x86_64.AppImage
```
1. Впиши **Релей URL** и **Токен**, нажми **Подключить**.
2. Отметь принтеры (при желании — «2-стор.»/«Ч/Б»), нажми **Добавить выбранные в CUPS**.
   - Если ты в группе CUPS-админов (`lpadmin`/`sys`/`wheel`) — добавятся сразу.
   - Если прав нет — клиент сохранит скрипт и покажет команду вида `sudo bash ~/.config/axoprint/add-printers.sh`; выполни её один раз в терминале (или добавь себя: `sudo usermod -aG lpadmin $USER` и перелогинься).
3. Готово: принтеры `AxoPrint_<имя>` видны в системе; печатай в них из любого приложения.
   CUPS сам отправит задание (PDF) на релей по `ipps://…/ipp/<token>/printers/<queue>`.

> Требует, чтобы на релее был включён IPP-сервер (он есть). Резидентного приложения на
> Linux держать не надо — печать идёт через CUPS напрямую.

## Как это печатается

1. Печатаешь в принтер `AxoPrint: <имя>` → драйвер *Microsoft Print to PDF* молча пишет
   PDF в `%ProgramData%\AxoPrint\spool\<queueId>.pdf`.
2. Клиент-трей видит файл и шлёт его на релей (`POST /api/print/<queueId>`).
3. Релей кладёт задание в очередь (на диск) и отдаёт ждущему агенту.
4. Агент скачивает PDF и печатает через `SumatraPDF.exe -print-to "<принтер>" -silent`.
5. Статус (Completed/Aborted) возвращается на релей.

Если агент офлайн — задания **копятся** в очереди и печатаются, когда он вернётся.
Если релей недоступен — PDF остаётся в спуле и уходит, как только связь вернётся.

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
