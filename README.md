# VoceLens 🎙️🔍

**VoceLens** — это Android-приложение на базе **.NET MAUI**, разработанное в виде плавающего интеллектуального ассистента чтения (Floating Button / System Alert Window) поверх любых сторонних приложений (читалок, браузеров, Telegram, PDF).

Приложение делает скриншот экрана (или выделенной пользователем области), отправляет его на Mistral OCR для распознавания текста, интеллектуально делит текст на смысловые фрагменты с сохранением границ предложений, отправляет их на Mistral TTS (`voxtral-mini-tts-2603`) для синтеза речи и воспроизводит аудио пользователю в фоновом режиме.

---

## 🌟 Ключевые возможности

1. **Плавающая кнопка поверх всех приложений (Floating Button / ChatHead)**:
   - Перетаскивается в любую точку экрана пальцем.
   - Одиночный тап:
     - В состоянии покоя: мгновенный захват экрана -> Mistral OCR -> умная нарезка -> Mistral TTS -> воспроизведение.
     - Во время воспроизведения: пауза / возобновление.
   - Длительное нажатие: показ / скрытие рамки захвата.
   - Динамическая цветовая индикация состояния:
     - 🟣 **Фиолетовый (Idle)** — ожидание действий.
     - 🟠 **Оранжевый (Capturing)** — захват экрана.
     - 🟡 **Желтый (OCR)** — сканирование изображения Mistral OCR.
     - 🔵 **Синий (Chunking)** — интеллектуальное разделение текста.
     - 🩵 **Голубой (TTS)** — генерация речи с указанием номера части (1/4, 2/4...).
     - 🟢 **Зеленый (Playing)** — озвучивание текущего фрагмента.
     - 🔴 **Красный (Error)** — ошибка с выводом подсказки.

2. **Плавающая настраиваемая рамка (OCR Preview Frame) поверх приложений**:
   - Пользователь может прямо поверх стороннего приложения (например, читалки FB2/ePub, сайта или мессенджера) настроить прямоугольную область:
     - Верхняя панель для перемещения окна.
     - Угловой маркер в правом нижнем углу для изменения ширины и высоты.
     - Кнопка **[Scan]** на рамке для мгновенного чтения именно этой области.
     - Кнопка **[X]** для закрытия рамки.
   - Координаты и размеры рамки автоматически сохраняются в настройках.

3. **Полная интеграция с облачными сервисами Mistral (по методикам проекта `Voce`)**:
   - **Mistral OCR**:
     - Эндпоинт: `POST https://api.mistral.ai/v1/ocr`
     - Модель по умолчанию: `mistral-ocr-latest`
     - Передача скриншота в формате Base64 Data URL (`data:image/jpeg;base64,...`).
     - Извлечение форматированного Markdown текста из страниц ответа.
   - **Интеллектуальный чанкинг (Text Chunking)**:
     - Точный перенос алгоритма `splitIntoChunks` из `Voce`:
     - Регулярное выражение `[^.!?]+[.!?]*|[^.!?]+` для сохранения границ предложений.
     - Если длина предложения превышает лимит (по умолчанию 500 символов), оно делится по пробелам без разрыва слов.
   - **Mistral Text-to-Speech (TTS)**:
     - Эндпоинт: `POST https://api.mistral.ai/v1/audio/speech`
     - Модель по умолчанию: `voxtral-mini-tts-2603`
     - Формат: MP3.
     - Фоновая предзагрузка (Preloading) следующего фрагмента во время проигрывания текущего — как в `Voce`!
   - **Получение и фильтрация голосов текущего пользователя**:
     - Эндпоинт: `GET https://api.mistral.ai/v1/audio/voices?limit=50&offset=0`
     - Фильтрация: отбираются **только** голоса, у которых заполнено поле `user_id` (`voice => !string.IsNullOrEmpty(voice.UserId)`), что в точности соответствует коду `Voce`:
       ```javascript
       return allVoices.filter(voice => voice.userId);
       ```
     - Выбор активного голоса в выпадающем списке (Picker).

4. **Экран настроек (Settings Page)**:
   - Ввод и безопасное сохранение Mistral API Key.
   - Кнопка загрузки персональных клонированных голосов пользователя.
   - Выбор модели OCR (`mistral-ocr-latest`) и TTS (`voxtral-mini-tts-2603`).
   - Настройка максимальной длины фрагмента (Max Chunk Size).
   - Переключатель непрерывного воспроизведения (Autoplay).
   - Управление системными разрешениями Android (Overlay, MediaProjection).
   - Тестовое распознавание и кнопки ручного управления плеером (Play / Pause / Stop).

---

## 🛠 Архитектура проекта

```text
VoceLens/
├── Models/
│   ├── VoiceItem.cs            # Модели списка голосов Mistral API (с фильтрацией user_id)
│   ├── OcrModels.cs            # DTO для Mistral OCR (запрос / страницы Markdown)
│   ├── SpeechModels.cs         # DTO для Mistral Speech (voxtral-mini-tts-2603)
│   ├── AppState.cs             # Состояния workflow и аргументы событий
│   └── OverlayBounds.cs        # Модель координат и размеров рамки захвата
├── Services/
│   ├── Mistral/
│   │   ├── IMistralClient.cs   # Интерфейс API Mistral
│   │   └── MistralClient.cs    # Реализация запросов к /v1/ocr, /v1/audio/speech, /v1/audio/voices
│   ├── Chunking/
│   │   ├── ITextChunker.cs     # Интерфейс чанкера
│   │   └── TextChunker.cs      # Точная C#-имплементация splitIntoChunks из Voce
│   ├── Audio/
│   │   ├── IPlatformAudioPlayer.cs # Кроссплатформенный интерфейс аудиоплеера
│   │   ├── NullAudioPlayer.cs      # Заглушка для сборки/десктопа
│   │   ├── IAudioPlaybackManager.cs
│   │   └── AudioPlaybackManager.cs # Координатор очереди фрагментов, кэширования MP3 и предзагрузки
│   ├── Overlay/
│   │   ├── IFloatingOverlayService.cs
│   │   └── NullFloatingOverlayService.cs
│   ├── Settings/
│   │   ├── IAppSettingsService.cs
│   │   └── AppSettingsService.cs   # Хранение настроек через MAUI Preferences
│   └── Workflow/
│       ├── IOcrSpeechWorkflowController.cs
│       └── OcrSpeechWorkflowController.cs # Главный оркестратор: Скриншот -> OCR -> Chunk -> TTS -> Play
├── ViewModels/
│   └── MainViewModel.cs        # MVVM модель управления экраном настроек
├── Platforms/
│   └── Android/
│       ├── Audio/
│       │   └── AndroidAudioPlayer.cs             # Нативный Android MediaPlayer
│       ├── Capture/
│       │   ├── ScreenCaptureManager.cs           # Захват экрана через MediaProjection + VirtualDisplay + обрезка
│       │   └── MediaProjectionPermissionActivity.cs # Прозрачная Activity запроса прав на снимок экрана
│       └── Overlay/
│           ├── OverlayPermissionHelper.cs        # Проверка и запрос SYSTEM_ALERT_WINDOW
│           ├── FloatingCropFrameView.cs          # Плавающее изменяемое окно-рамка поверх приложений
│           ├── FloatingOverlayService.cs         # Foreground Service плавающей кнопки и рамки
│           └── AndroidFloatingOverlayService.cs  # Мост между MAUI и сервисом оверлея
├── MainPage.xaml / .cs         # Главный UI настроек
└── MauiProgram.cs              # DI-контейнер и регистрация сервисов
```

---

## 📱 Системные разрешения Android

Для полноценной работы плавающей кнопки и захвата экрана используются разрешения:
- `android.permission.SYSTEM_ALERT_WINDOW` — отображение кнопки и рамки поверх любых приложений.
- `android.permission.FOREGROUND_SERVICE` — обеспечение работы сервиса в фоне без засыпания.
- `android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION` — захват пикселей экрана.
- `android.permission.INTERNET` — обращение к API Mistral.

---

## 🚀 Запуск и сборка

1. Откройте решение или проект `VoceLens.csproj` в Visual Studio 2022+ или через .NET CLI.
2. Сборка Android пакета:
   ```bash
   dotnet build -f net10.0-android
   ```
3. Запуск на подключенном Android устройстве или эмуляторе:
   ```bash
   dotnet run -f net10.0-android
   ```
4. В приложении:
   - Введите ваш **Mistral API Key**.
   - Нажмите **"Fetch My Custom Voices"** и выберите нужный голос в списке.
   - Предоставьте разрешение **"Display Over Other Apps"** и авторизуйте **"Screen Capture"**.
   - Нажмите **"Adjust OCR Frame Over Apps"** для настройки прямоугольника чтения.
   - Нажмите **"Floating Button"** — переключитесь в любое приложение и читайте текст одним тапом!
