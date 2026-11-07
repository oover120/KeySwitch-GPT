# Архитектура

`KeySwitch.App` — STA WPF, трей, меню, настройки, автозапуск, координация сеанса.

`KeySwitch.Hooks` — Win32 interop, отдельный поток с message loop для WH_KEYBOARD_LL / WH_MOUSE_LL и WinEvent, асинхронная проверка UI Automation с бюджетом 50 мс, снимок фокуса, SendInput и транзакция замены, буфер обмена.

`KeySwitch.Core` — независимые от Windows физические таблицы, ограниченный буфер слова, LanguageProfile, частотный/n-граммный детектор, политика исключений, отмена и текстовые правила. Новый язык регистрируется через профиль и таблицу физических символов.

`KeySwitch.Data` — встроенные сжатые языковые данные и атомарное JSON-хранилище `%APPDATA%/KeySwitch`; настройки, счётчики обучения и словарь замен. Запись выполняется после явного действия/обучения, никогда из хука.

`KeySwitch.Tests` — xUnit, регрессии и ≥500 слов в обеих раскладках. `tools/Evaluate` — воспроизводимые метрики отдельно на известных словах и внешнем holdout-корпусе; ненулевой exit code при непройденных порогах.

## Поток данных

WinEvent → invalidate focus epoch → UIA worker → immutable WindowContext.

LL keyboard → modifier state + WordBuffer → detector at delimiter → reserved transaction → layout request/ack → one SendInput batch → replay deferred input → undo token.

Буфер ввода, снимки буфера обмена и токен отмены живут в памяти. Снимок фокуса разделяет сведения Win32 и результат UI Automation. Диагностика включается явно и уходит в отдельную очередь фоновой записи. Схема диагностического журнала не содержит текста, заголовков и URL.
