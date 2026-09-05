using Microsoft.Extensions.Logging;
using VoceLens.Services.Audio;
using VoceLens.Services.Chunking;
using VoceLens.Services.Logging;
using VoceLens.Services.Mistral;
using VoceLens.Services.Overlay;
using VoceLens.Services.Settings;
using VoceLens.Services.Workflow;
using VoceLens.ViewModels;

#if ANDROID
using VoceLens.Platforms.Android.Audio;
using VoceLens.Platforms.Android.Overlay;
#endif

namespace VoceLens;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Core Services
		builder.Services.AddSingleton<IAppLogService, AppLogService>();
		builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
		builder.Services.AddSingleton<IMistralClient, MistralClient>();
		builder.Services.AddSingleton<ITextChunker, TextChunker>();

#if ANDROID
		builder.Services.AddSingleton<IPlatformAudioPlayer, AndroidAudioPlayer>();
		builder.Services.AddSingleton<IFloatingOverlayService, AndroidFloatingOverlayService>();
#else
		builder.Services.AddSingleton<IPlatformAudioPlayer, NullAudioPlayer>();
		builder.Services.AddSingleton<IFloatingOverlayService, NullFloatingOverlayService>();
#endif

		builder.Services.AddSingleton<IAudioPlaybackManager, AudioPlaybackManager>();
		builder.Services.AddSingleton<IOcrSpeechWorkflowController, OcrSpeechWorkflowController>();

		// ViewModels & Pages
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
