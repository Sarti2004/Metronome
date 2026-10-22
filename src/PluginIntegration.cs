using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using Metronome.Actions;
using Serilog;

namespace Metronome;

/// <summary>
/// The plugin's one integration. It provides the metronome deck widget; add actions to
/// <see cref="Actions"/>, and opt into a capability by implementing its interface here
/// (<c>IVariableProvider</c>, <c>IEventProvider</c>, <c>IConfigFlowProvider</c>, and so on).
/// </summary>
public sealed class PluginIntegration : IPluginIntegration, IWidgetTypeProvider, IUiProvider
{
	private const string BackgroundResource = "brushed-metal";

	private static readonly WidgetTypeDescriptor MetronomeType = new(
		"metronome",
		Strings.Widgets.Metronome.Name(),
		Strings.Widgets.Metronome.Description(),
		DefaultData: JsonSerializer.Serialize(new Dictionary<string, string> { [ClickSounds.DataKey] = ClickSounds.Default }),
		DataSchema: ClickSounds.DataSchema,
		HasConfiguration: true,
		Metadata: null);

	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<Guid, int> _tempos = new();
	private readonly ConcurrentDictionary<MetronomeWidget, byte> _openWidgets = new();
	private readonly ClickPlayer _clickPlayer;
	private readonly ImageGenerator _imageGenerator;
	private volatile string? _metronomeTypeId;
	private volatile UiResource? _background;

	// Built by DI, so anything the container knows can be taken here: IHttpClientFactory, IOptions<T>,
	// PluginMetadata, IPluginCatalogNotifier.
	public PluginIntegration(ILogger logger, ClickPlayer clickPlayer, ImageGenerator imageGenerator)
	{
		_logger = logger.ForContext<PluginIntegration>();
		_clickPlayer = clickPlayer;
		_imageGenerator = imageGenerator;
		Actions = [new SetTempoAction(this), new StartStopAction(this)];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string ProviderName => "Metronome";

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	/// <summary>
	/// Runs once the session is established, and again after a non-resume reconnect or a configuration
	/// change, so it has to be safe to run repeatedly against an already-initialized process.
	/// </summary>
	public async Task InitializeAsync(IIntegrationContext context)
	{
		_imageGenerator.Attach(context.UiResources);
		try
		{
			var image = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "brushed-metal.jpg"));
			_background = await context.UiResources.RegisterAsync(BackgroundResource, image, "image/jpeg", CancellationToken.None);
		}
		catch (Exception ex) when (ex is IOException or UiResourceException or HostInvocationException)
		{
			_logger.Warning(ex, "Could not register the metronome background, widgets render without it.");
		}

		_logger.Information("Initialized.");
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)
	{
		var registration = await context.RegisterWidgetTypeAsync(MetronomeType, cancellationToken);
		_metronomeTypeId = registration.WidgetTypeId;
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [MetronomeType];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (IsMetronomeConfig(surface))
		{
			surface.Attributes.TryGetValue(UiConfigSurfaceAttributes.WidgetData, out var data);
			var config = MetronomeConfigView.Build(data, _clickPlayer);
			return Task.FromResult<IUiSession?>(new ViewSession(new UiView(surface, config)));
		}

		var isMetronome = surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview
			&& surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetType, out var type)
			&& type.GetString() == _metronomeTypeId;
		if (!isMetronome)
		{
			return Task.FromResult<IUiSession?>(null);
		}
		
		var widgetId = UiWidgetSurface.WidgetId(surface);
		var tempo = widgetId is { } id && _tempos.TryGetValue(id, out var saved) ? saved : MetronomeWidget.DefaultTempo;
		var widget = new MetronomeWidget(
			tempo,
			value =>
			{
				if (widgetId is { } key)
				{
					_tempos[key] = value;
				}
			},
			_clickPlayer,
			ClickSounds.Read(UiWidgetSurface.Data(surface)),
			_imageGenerator.GetAsync);

		var root = widget.Build(_background, UiWidgetSafeArea.For(surface));
		IDisposable owner = widget;
		
		if (surface.Kind == UiSurfaceKinds.Widget)
		{
			_openWidgets[widget] = 0;
			owner = new OpenWidget(_openWidgets, widget);
		}

		return Task.FromResult<IUiSession?>(new ViewSession(new UiView(surface, root), owner));
	}

	internal IReadOnlyList<MetronomeWidget> GetOpenWidgets() => [.. _openWidgets.Keys];

	private bool IsMetronomeConfig(UiSurface surface)
		=> surface.Kind == UiSurfaceKinds.Config
			&& surface.Attributes.TryGetValue(UiConfigSurfaceAttributes.EntryPoint, out var entryPoint)
			&& entryPoint.GetString() == UiConfigEntryPoints.WidgetConfig
			&& surface.Attributes.TryGetValue(UiConfigSurfaceAttributes.WidgetType, out var type)
			&& type.GetString() == _metronomeTypeId;

	public Task ShutdownAsync() => Task.CompletedTask;

	private sealed class OpenWidget(ConcurrentDictionary<MetronomeWidget, byte> openWidgets, MetronomeWidget widget) : IDisposable
	{
		public void Dispose()
		{
			openWidgets.TryRemove(widget, out _);
			widget.Dispose();
		}
	}
}
