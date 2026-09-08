# VoceLens 🎙️🔍

**VoceLens** is an Android application built on **.NET MAUI (C# 13, .NET 10)**, engineered as an intelligent floating reading assistant (**Floating Button / System Alert Window**) that runs seamlessly on top of any third-party app (e-book readers, web browsers, Telegram, PDF/FB2/ePub documents).

The app captures the screen, transmits it to **Mistral OCR** for high-precision text recognition, cleanses the transcript of graphic Markdown artifacts, intelligently segments text along sentence boundaries, synthesizes lifelike speech via **Mistral TTS (Voxtral)**, and streams audio in the background with continuous preloading of upcoming chunks.

---

## 🌟 Key Features

### 1. 🔘 Intelligent Floating Button
- **Always Accessible**: Floats above all apps and effortlessly drags to any position on the screen.
- **Intuitive Gesture Controls**:
  - **Single Tap**:
    - *When idle* — captures screen under the button ➔ OCR ➔ sanitization ➔ chunking ➔ speech playback.
    - *During playback* — pause / resume reading.
  - **Quick Double Tap**: instantly stops reading and clears the chunk queue (`Stop & Clear`).
  - **Mini Red Stop Button**: a sleek circular button with a centered white square appearing in the top corner of the overlay during playback for one-click termination.
  - **Long Press (Hold 550 ms)**: full application shutdown (`Close & Exit`) — hides the floating button, terminates Android screen monitoring (`MediaProjection`), unloads foreground services, and exits the process.
- **Dynamic Color Status Feedback**:
  - 🟣 **Purple (Ready)** — idle, ready to capture.
  - 🟠 **Orange (Capturing)** — grabbing screen frame.
  - 🟡 **Yellow (Scanning OCR)** — recognizing text via Mistral OCR.
  - 🔵 **Blue (Chunking)** — intelligent sentence splitting.
  - 🩵 **Cyan (Generating Audio)** — synthesizing speech with progress indicator (e.g. `1/4`).
  - 🟢 **Green (Playing)** — active audio playback.
  - 🔴 **Red (Error)** — error indicator with automatic status recovery.

### 2. 🛡️ Artifact Defense & Reliable Capture
- **Guaranteed Button Concealment**: Before capturing the screen, all queued buffer frames in `ImageReader` are drained, overlay opacity is set to zero (`Alpha = 0`), and a 250 ms sync delay ensures Android's `SurfaceFlinger` compositor renders the underlying content without any button artifacts.
- **Smart Text Sanitizer (`TextSanitizer`)**: Automatically strips Markdown image tags such as `![img-0.jpeg](img-0.jpeg)` produced by Mistral OCR when icons, illustrations, or graphics are detected. This ensures Text-to-Speech never vocalizes technical markup.
- **Persistent MediaProjection Session**: Virtual display and projection sessions are established once and reused indefinitely, avoiding repetitive system permission prompts on Android 14+.

### 3. 📱 Interactive Crop Calibration
- Exclude the Android status bar, book title headers, navigation bar, and page numbers from recognition.
- **Interactive Smartphone Bezel Mockup in Main UI**:
  - **«📸 Reader Screenshot»**: minimizes VoceLens, captures the active e-reader screen, and returns directly to the calibration view.
  - **«🔄 Last Screenshot»**: loads the most recently captured screen frame.
  - **Visual Exclusion Bars**: semi-transparent red overlays highlight excluded margins in real time.
  - **Percentage-Based Sliders**: fine-tune Top, Bottom, Left, and Right margins with immediate feedback.

### 4. 🧠 Mistral AI Cloud Integration
- **Mistral OCR**:
  - Default model: `mistral-ocr-latest`.
  - Base64 Data URL transport.
  - Structured Markdown output extraction.
- **Mistral Text-to-Speech (TTS)**:
  - Model: `voxtral-mini-tts-2603`.
  - High-fidelity MP3 streaming.
  - **Continuous Preloading**: automatically requests and caches the next audio chunk while the current one is playing, ensuring smooth, seamless reading without gaps between sentences.
- **Cloned Custom Voices Support**:
  - Queries user-specific cloned voices (`user_id`).
  - Interactive voice selector dropdown in settings.
- **Intelligent Chunking (`splitIntoChunks`)**:
  - Preserves grammatical sentence boundaries (`.`, `!`, `?`) and enforces character limits without cutting words in half.

### 5. 🤖 Native Android TTS Fallback (Censorship & Failure Resilience)
- **Zero Content Moderation**: Built-in protection for reading literature containing mature, romantic, or erotic scenes where cloud models (like Mistral) enforce content moderation refusals.
- **Graceful Failover**: If Mistral TTS rejects a text chunk or encounters an error, VoceLens seamlessly routes that chunk to Android's local Text-to-Speech synthesizer and auto-advances to subsequent chunks without interrupting reading.
- **Customizable Fallback Settings**:
  - Master toggle (`Enable Android TTS Fallback`, enabled by default).
  - Language selection (defaults to device system language, with all installed Android locales available).
  - Voice selection (dynamically enumerates all local and network voices installed on the device).
  - Audio testing button (`🔊 Test Android Fallback Speech`) with live playback control.

### 6. 📋 Real-Time Event Log & Transcript Display
- **LAST EXTRACTED OCR TEXT**: displays the latest recognized and sanitized text directly on the dashboard.
- **APPLICATION LOG**: embedded scrollable terminal showing the last 500 diagnostic events with level-based color coding (`INFO`, `WARN`, `ERROR`):
  - **«📋 Copy»** — copies complete event history to the clipboard.
  - **«🗑️ Clear»** — clears terminal buffer immediately.

---

## 🏗 Project Architecture

```text
VoceLens/
├── Models/
│   ├── AppState.cs                 # Workflow states and event args
│   ├── OcrModels.cs                # Mistral OCR request & response DTOs
│   ├── OverlayBounds.cs            # CropFrameBounds margin models
│   ├── SpeechModels.cs             # Mistral TTS request DTOs
│   └── VoiceItem.cs                # Mistral Voice API models
├── Platforms/
│   └── Android/
│       ├── Audio/
│       │   └── AndroidAudioPlayer.cs   # Native Android MediaPlayer wrapper
│       ├── Capture/
│       │   ├── MediaProjectionPermissionActivity.cs # Invisible permission dispatcher
│       │   └── ScreenCaptureManager.cs # VirtualDisplay, MediaProjection & frame cropping
│       └── Overlay/
│           ├── AndroidFloatingOverlayService.cs # MAUI-to-Android service bridge
│           ├── FloatingOverlayService.cs        # Foreground Service managing overlay & gestures
│           └── OverlayPermissionHelper.cs       # SYSTEM_ALERT_WINDOW validator
├── Resources/
│   ├── AppIcon/                    # Adaptive icon (Safe Zone for Android 12+)
│   ├── Splash/                     # Splash screen on navy background (#0B1829)
│   └── Styles/                     # MAUI color palette and control templates
├── Services/
│   ├── Audio/
│   │   ├── AudioPlaybackManager.cs # Playback queue, MP3 cache & chunk preloader
│   │   ├── IAudioPlaybackManager.cs
│   │   ├── INativeTtsService.cs    # Local Android Text-to-Speech contract
│   │   ├── IPlatformAudioPlayer.cs
│   │   └── NativeTtsService.cs     # Native Android TTS fallback engine with locale/voice selection
│   ├── Chunking/
│   │   ├── ITextChunker.cs
│   │   ├── TextChunker.cs          # splitIntoChunks sentence boundary splitter
│   │   └── TextSanitizer.cs        # Regex filter removing OCR markdown image tags
│   ├── Logging/
│   │   └── AppLog.cs               # Thread-safe in-memory event logger
│   ├── Mistral/
│   │   ├── IMistralClient.cs
│   │   └── MistralClient.cs        # HTTP client for /ocr, /audio/speech, /audio/voices
│   ├── Overlay/
│   │   ├── IFloatingOverlayService.cs
│   │   └── NullFloatingOverlayService.cs
│   ├── Settings/
│   │   ├── AppSettingsService.cs   # Persistent settings via MAUI Preferences
│   │   └── IAppSettingsService.cs
│   └── Workflow/
│       ├── IOcrSpeechWorkflowController.cs
│       └── OcrSpeechWorkflowController.cs # Orchestrator: Capture -> OCR -> Filter -> TTS -> Audio
├── ViewModels/
│   └── MainViewModel.cs            # Settings, calibration & log view model
├── MainPage.xaml / .cs             # Main dashboard UI
├── MauiProgram.cs                  # Entry point, DI container & service registrations
└── VoceLens.csproj                 # .NET MAUI project file (net10.0-android)
```

---

## 🔒 Android Permissions

VoceLens requires the following permissions to operate:
1. **Display over other apps (`SYSTEM_ALERT_WINDOW`)** — renders the floating button over e-reader apps and web browsers.
2. **Screen Capture (`FOREGROUND_SERVICE_MEDIA_PROJECTION`)** — captures display frames for OCR processing.
3. **Foreground Service (`FOREGROUND_SERVICE`)** — prevents Android OS from terminating playback when the screen is turned off.
4. **Internet Access (`INTERNET`)** — connects to Mistral AI endpoints for OCR and speech generation.

---

## 🚀 Building & Installation

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) with `maui-android` workload.
- Android SDK (API 34/35) and Java SDK 17+.

### CLI Build
```powershell
# Build signed release-ready APK for Android
dotnet build -f net10.0-android -p:EmbedAssembliesIntoApk=true
```

Output APK will be generated at:
`bin/Debug/net10.0-android/com.companyname.vocelens-Signed.apk`

### Install via ADB
```powershell
adb install -r bin/Debug/net10.0-android/com.companyname.vocelens-Signed.apk
```

---

## 📖 User Guide

1. **Initial Setup**:
   - Open **VoceLens**.
   - Enter your **Mistral API Key** (securely saved in device storage).
   - Tap **«Fetch Custom Voices»** to load cloned voices and choose your preferred narrator.
2. **Grant Permissions**:
   - Tap **«Grant Overlay Permission»** and toggle permission for VoceLens.
   - Tap **«Authorize Screen Capture»** to approve initial projection access.
3. **Calibrate Margins (Optional)**:
   - Under **Interactive Crop Calibration**, adjust exclusion margins (status bar, navigation bar, headers/footers).
4. **Read Any Content**:
   - Tap **«Start Floating Button»** — the floating orb appears on your screen.
   - Open any book reader (Kindle, Moon+ Reader, FBReader), document, or browser page.
   - Tap the floating button once — VoceLens captures the screen, recognizes the text, and reads aloud smoothly!
   - Tap once to pause or resume.
   - Tap the mini red **Stop** button or double-tap the button to reset playback.
   - Long-press the button (550 ms) to completely exit the app and stop screen monitoring.
