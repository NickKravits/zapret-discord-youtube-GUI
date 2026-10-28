# Zapret GUI

Графическое приложение для обхода блокировок Discord и YouTube в стиле «Параметров» Windows 11.

Это форк [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Стратегии, списки и бинарные файлы берутся из оригинального репозитория без изменений, а вместо `.bat`-файлов и меню `service.bat` используется окно с настройками. Основа обхода — [zapret](https://github.com/bol-van/zapret) от bol-van.

## Возможности

- **Стратегии** — список всех `general*.bat`, включение и выключение в один клик, просмотр аргументов `winws.exe`.
- **Автозапуск обхода** — служба `zapret`, совместимая с `service.bat`: стратегия работает с момента загрузки Windows, даже если приложение не открыто. На Linux — служба systemd `zapret-gui`.
- **Автозапуск приложения** — при входе в систему, без запроса UAC, сразу в трей.
- **Comss DNS** — подключение [Comss.one DNS](https://www.comss.ru/page.php?id=7315) в один клик: обычный DNS или DNS-over-HTTPS (Windows 11). Отключение возвращает автоматический DNS.
- **Тест стратегий** — стратегии запускаются по очереди и проверяются на Discord, YouTube, Google и Cloudflare (HTTP/1.1, TLS 1.2, TLS 1.3, ping). Лучшая включается одной кнопкой.
- **Списки доменов и IP** — быстрое добавление доменов, ссылок, IP-адресов и подсетей (тип определяется автоматически), поиск и удаление в любом списке.
- **Консоль** — журнал работы `winws`/`nfqws` и всех действий приложения.
- **Параметры** — Game Filter, режим IPSet, обновление списка IPSet и файлов zapret, диагностика, тема оформления.

## Windows

### Требования

Windows 10 или 11, x64, права администратора.

### Установка

1. Скачайте `ZapretGUI-<версия>-win-x64.zip` со страницы релизов.
2. Распакуйте архив в папку **без кириллицы и не в OneDrive**, например `C:\ZapretGUI`.
3. Запустите `ZapretGUI.exe` и разрешите запуск от имени администратора.
4. На главной выберите стратегию и нажмите **«Включить»**.

Если Discord или YouTube не открываются, перейдите в **«Тест стратегий»**, нажмите **«Начать тест»** и включите стратегию с пометкой «Лучшая».

Чтобы обход работал всегда, включите **«Автозапуск обхода при старте системы»** на главной. Приложение после этого можно закрыть.

### Полезно знать

- Файлы zapret лежат в папке `zapret` рядом с `ZapretGUI.exe`. Если её нет, приложение предложит скачать последнюю версию.
- Антивирусы иногда ругаются на `WinDivert64.sys` и `winws.exe`. Это ложное срабатывание, подробнее в [README оригинального репозитория](https://github.com/Flowseal/zapret-discord-youtube#readme). Добавьте папку в исключения.
- Не запускайте одновременно другие обходы (GoodbyeDPI, другие сборки zapret) и VPN — они конфликтуют. Проверить это поможет **«Параметры → Диагностика»**.
- Приложение совместимо с `service.bat`: служба, установленная через GUI, видна в `service.bat`, и наоборот.

### Удаление

1. На главной выключите **«Автозапуск обхода при старте системы»** — служба будет удалена.
2. В **«Параметрах»** выключите **«Запускать приложение при входе в систему»**.
3. Если включали Comss DNS — нажмите **«Отключить»** на странице «Comss DNS».
4. Закройте приложение через значок в трее и удалите папку.

## Linux

Поддержка экспериментальная. Вместо `winws` используется `nfqws` из bol-van/zapret, перехват трафика — через nftables.

### Требования

- x64, systemd
- пакеты `nftables` и `polkit` (окно ввода пароля `pkexec`)
- модуль ядра `nfnetlink_queue` (есть почти во всех дистрибутивах)

Debian/Ubuntu:

```bash
sudo apt install nftables pkexec
```

Fedora:

```bash
sudo dnf install nftables polkit
```

Arch:

```bash
sudo pacman -S nftables polkit
```

### Установка

```bash
tar -xzf ZapretGUI-<версия>-linux-x64.tar.gz
```

```bash
cd ZapretGUI-<версия>-linux-x64 && ./install.sh
```

Приложение появится в меню как **Zapret GUI**, из терминала запускается командой `zapret-gui`. Можно запускать и без установки: `./ZapretGUI` из распакованной папки.

### Первый запуск

1. Откройте **«Параметры» → «nfqws (Linux)»** и нажмите **«Скачать nfqws»** (или укажите путь к уже установленному `nfqws`).
2. На главной выберите стратегию и нажмите **«Включить»**. Система один раз за сеанс спросит пароль администратора.

Где что хранится:

- настройки и файлы zapret — `~/.local/share/zapret-gui`;
- рабочая копия для `nfqws` — `/opt/zapret-gui`;
- служба автозапуска — `/etc/systemd/system/zapret-gui.service`;
- Comss DNS настраивается через systemd-resolved (`/etc/systemd/resolved.conf.d/zapret-gui-comss.conf`).

### Удаление

```bash
./uninstall.sh
```

## Сборка из исходников

Нужен [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Сборка и упаковка релиза (Windows, результат в `dist/release`):

```powershell
./build.ps1
```

Только сборка под Linux на Linux:

```bash
./build.sh
```

Запуск для разработки (без запроса прав администратора):

```bash
dotnet run --project src/ZapretGui
```

## Благодарности

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — оригинальный проект: стратегии, списки, `service.bat`
- [bol-van/zapret](https://github.com/bol-van/zapret) — `winws` и `nfqws`
- [Comss.one DNS](https://www.comss.ru/page.php?id=7315)
- [Avalonia](https://avaloniaui.net/), [FluentAvalonia](https://github.com/amwx/FluentAvalonia)

## Лицензия

[MIT](LICENSE). Файлы zapret-discord-youtube и zapret распространяются под лицензиями своих авторов.
