Shutdown Tray 1.2.1 — полный автономный архив для Windows x64

1. Распакуйте ВСЮ папку в постоянное место. Не запускайте EXE прямо из ZIP.
2. Завершите старую копию Shutdown через «Выход» в трее.
3. Запустите Shutdown.exe из новой папки.

Ничего дополнительно устанавливать не нужно: .NET и Windows App Runtime включены.
Старые настройки подхватятся из %LOCALAPPDATA%\Shutdown\settings.json автоматически.
При сохранении создается резервная копия settings.json.bak.
Если включен автозапуск, после обычного запуска он будет указывать на новую папку.
Для будущих небольших обновлений сохраняйте эту полную папку со всеми библиотеками.

Двойной щелчок по значку выполняет главное действие текущей сессии.
Правый щелчок открывает меню. Настройки доступны через пункт «Настройки».
Список действий общий. Главное действие выбирается отдельно для локального
и удаленного сеанса; отключение от сеанса доступно только в RDP.

Full self-contained Windows x64 build. Extract the entire folder, exit the old
Shutdown instance, then run Shutdown.exe. No runtime installation is required.
Existing settings are migrated automatically; Save creates settings.json.bak.
If autostart is enabled, launching the app updates its path to the new folder.

Автор / Author: sol669
https://github.com/sol669/Shutdown
