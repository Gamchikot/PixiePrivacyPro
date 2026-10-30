# Changelog

All notable changes are listed here. Versions follow `version.txt`.

## [0.15.7] — 2026-10-30

**English**

- An unexpected error no longer stops the program silently: it is written to the journal and shown in the tray, and the window keeps watching the camera.
- The installer is about 10 times smaller: the AForge.NET source code is published next to it in each release instead of inside it.
- The virtual camera's debug log moved from the shared ProgramData folder to the temp folder of the account that loads it.
- The placeholder is sent to the virtual cameras once a second while no app reads them (it was 8 times a second).
- Build: GitHub Actions are pinned to exact commits. English version of the detailed description (docs/DETAILS.md).

**Русский**

- Неожиданная ошибка больше не останавливает программу молча: она пишется в журнал и показывается в трее, а окно продолжает следить за камерой.
- Установщик примерно в 10 раз меньше: исходники AForge.NET выкладываются рядом с ним в каждом релизе, а не внутри.
- Отладочный журнал виртуальной камеры перенесён из общей папки ProgramData во временную папку учётной записи, которая его загружает.
- Пока виртуальные камеры никто не читает, заставка отправляется раз в секунду (было 8 раз в секунду).
- Сборка: действия GitHub Actions закреплены на точных коммитах. Подробное описание на английском (docs/DETAILS.md).

## [0.15.6] — 2026-10-30

**English**

- Journal: a video stream starting or stopping is no longer marked "changed outside Pixie Pro" (own openings were shown as outside ones).
- Trusted apps: the hint says apps are matched by file name.
- README: honest wording for the microphone switch — it stays off until you, or a program running as you, turn it back on.

**Русский**

- Журнал: начало и конец видеопотока больше не помечаются «изменено не через Pixie Pro» (свои открытия выглядели чужими).
- Доверенные программы: подсказка говорит, что программа узнаётся по имени файла.
- README: честнее про микрофон — он выключен, пока его не включите вы или программа, работающая от вашего имени.

## [0.15.5] — 2026-10-29

First public release. / Первый публичный релиз.

**English**

- One-click privacy for the EMEET PIXY: window orb, tray icon, tray control center and a global hotkey.
- Pixie virtual camera (DirectShow and Media Foundation) with a lock or custom placeholder while the lens is closed,
  frames 1:1, 4:3, 16:9 and circle, and a choice between showing the placeholder and opening the lens right away (optionally for trusted apps only).
- "I see you" warning when the lens opens outside Pixie Pro, with separate picture and sound switches.
- Microphone protection service that disables only the PIXY recording device; or microphone on with the chosen processing.
- Auto-close when unused, on lock, sleep and screen off; camera timer; privacy at power-on.
- Today's statistics and a searchable journal of camera and microphone use (microphone use is logged only while the PIXY microphone is on), with a Clear button.
- Windows 11 look, light and dark themes, English and Russian interface; one MSI that follows the Windows language.

**Русский**

- Приватность PIXY в один клик: шар в окне, значок и пункт управления в трее, горячая клавиша.
- Виртуальная камера Pixie (DirectShow и Media Foundation) с заставкой-замком или своей картинкой, пока объектив закрыт,
  рамки 1:1, 4:3, 16:9 и «Круг», выбор между заставкой и открытием объектива сразу (можно только для доверенных программ).
- Предупреждение «Я вижу тебя», если объектив открыли не через Pixie Pro; картинка и звук включаются отдельно.
- Служба защиты микрофона: отключает только устройство записи PIXY, либо микрофон включён с выбранной обработкой.
- Автозакрытие без использования, при блокировке, сне и выключении экрана; таймер камеры; приватность при включении компьютера.
- Статистика за сегодня и журнал с поиском (кто пользуется микрофоном, записывается только пока микрофон PIXY включён) и кнопкой «Очистить».
- Стиль Windows 11, светлая и тёмная темы, русский и английский интерфейс; один MSI, который выбирает язык по Windows.

[0.15.7]: https://github.com/Gamchikot/PixiePrivacyPro/releases/tag/v0.15.7
[0.15.6]: https://github.com/Gamchikot/PixiePrivacyPro/releases/tag/v0.15.6
[0.15.5]: https://github.com/Gamchikot/PixiePrivacyPro/releases/tag/v0.15.5
