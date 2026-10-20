using System.Diagnostics;
using System.Globalization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;

namespace Metronome;

public sealed class MetronomeWidget : IDisposable
{
	public const int DefaultTempo = 120;
	public const int MinTempo = 30;
	public const int MaxTempo = 300;
	private const int TempoStep = 5;
	private const int FineTempoStep = 1;
	private const double BeatLightSize = 0.065;
	private const int MaxTapsAveraged = 5;
	private const int FaderTicks = 11;
	private const double CapWidth = 0.09;
	private const double StepKeySize = 0.18;

	private const string MetalColor = "#5c5c5e";
	private const string BezelColor = "#0d0d0f";
	private const string KeyTopColor = "#3b3b40";
	private const string KeyBottomColor = "#222226";
	private const string KeyEdgeColor = "#55555c";
	private const string LabelColor = "#e8e8e8";
	internal const string AccentColor = "#f5a623";
	internal const string LedOffColor = "#3a2e1a";
	private const string LcdTopColor = "#c3cf94";
	private const string LcdBottomColor = "#9eae6c";
	private const string LcdTextColor = "#1c2410";
	private const string TickColor = "#6b6b72";

	// Slightly longer than one beat at MinTempo, so the slowest tempo can still be tapped.
	private static readonly TimeSpan TapTimeout = TimeSpan.FromSeconds(2.5);
	private static readonly TimeSpan TempoSettleDelay = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan SpinWindow = TimeSpan.FromMilliseconds(OperatingSystem.IsWindows() ? 16 : 2);
	private static readonly UiSize Gap = UiSize.FromBasis(0.04);
	private static readonly UiSize Hairline = UiSize.Capped(1.5 / UiLength.Cell, 1.5);
	private static readonly UiSize BezelWidth = UiSize.FromBasis(0.012);
	private static readonly UiSize CapRadius = UiSize.FromBasis(0.025);
	private static readonly UiSize BezelRadius = UiSize.FromBasis(0.035);

	private readonly UiState<int> _tempo;
	private readonly UiState<bool> _tapping;
	private readonly UiState<bool> _playing;
	private readonly UiState<UiResource?> _beatImage = new(null);
	private readonly Lock _beatImageGate = new();
	private readonly List<long> _taps = new(MaxTapsAveraged);
	private readonly Action<int> _tempoChanged;
	private readonly ClickPlayer? _clickPlayer;
	private readonly string _sound;
	private readonly Func<int, Task<UiResource?>>? _beatImages;
	private int _beatImageVersion;
	private double _faderWidth;
	private CancellationTokenSource? _ticker;
	
	public MetronomeWidget(
		int tempo,
		Action<int> tempoChanged,
		ClickPlayer? clickPlayer,
		string sound,
		Func<int, Task<UiResource?>>? beatImages,
		bool tapping = false,
		bool playing = false)
	{
		_clickPlayer = clickPlayer;
		_sound = sound;
		_beatImages = beatImages;
		_tempo = new UiState<int>(Math.Clamp(tempo, MinTempo, MaxTempo));
		_tapping = new UiState<bool>(tapping);
		_playing = new UiState<bool>(playing);
		_tempoChanged = tempoChanged;
	}

	private double Level => (double)(_tempo.Value - MinTempo) / (MaxTempo - MinTempo);

	public UiElement Build(UiResource? background, UiSize safeArea) => new UiButton
	{
		Key = "metronome",
		Corner = UiComponentButtonCorners.Tile,
		Background = MetalColor,
		Source = background is null ? UiValue.None<UiResource>() : UiValue.Of(background),
		Fit = UiComponentImageFits.Cover,
		Padding = safeArea,
		Children =
		[
			new UiResponsive
			{
				Key = "layout",
				Fill = true,
				Default = Compact(),
				Variants =
				[
					new UiResponsiveVariant { MinAspect = 1.5, Content = Landscape() },
					new UiResponsiveVariant { MaxAspect = 0.67, Content = Portrait() },
				],
			},
		],
	};

	private UiStack Landscape() => Column("landscape",
		Row("top", 0.4, StepKeys(-FineTempoStep, -TempoStep), Display(), StepKeys(FineTempoStep, TempoStep)),
		Row("bottom", 0.32, BeatIndicator(), Fader(), PlayKey(0.32), TapKey(0.32)));

	private UiStack Compact() => Column("compact",
		Display(0.36),
		Row("keys", 0.24, StepKeys(-TempoStep), PlayKey(), TapKey(), StepKeys(TempoStep)),
		Row("fader-row", 0.2, BeatIndicator(), Fader()));

	private UiStack Portrait() => Column("portrait",
		Display(0.7, digitSize: 0.3),
		Row("keys", 0.24, StepKeys(-TempoStep), PlayKey(), TapKey(), StepKeys(TempoStep)),
		Row("fader-row", 0.24, BeatIndicator(), Fader()));

	private static UiStack Column(string key, params UiElement[] children) => new()
	{
		Key = key,
		Justify = UiComponentJustify.Center,
		Gap = Gap,
		Children = children,
	};

	private static UiStack Row(string key, double mainSize, params UiElement[] children) => new()
	{
		Key = key,
		Direction = UiComponentDirections.Horizontal,
		MainSize = UiSize.FromBasis(mainSize),
		Gap = Gap,
		Children = children,
	};

	private UiModifier Display(double? mainSize = null, double digitSize = 0.2) => new()
	{
		Key = "display-glass",
		Radius = CapRadius,
		BorderWidth = BezelWidth,
		BorderColor = BezelColor,
		Background = VerticalGradient(LcdTopColor, LcdBottomColor),
		Child = new UiStack
		{
			Key = "display",
			MainSize = Extent(mainSize),
			Fill = mainSize is null,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children =
			[
				new UiTextRun
				{
					Key = "tempo",
					Text = UiText.From(() => _tempo.Value.ToString(CultureInfo.InvariantCulture)),
					Size = UiSize.Capped(digitSize, 30),
					Weight = UiComponentTextWeights.SemiBold,
					Color = LcdTextColor,
					Digits = 3,
					Align = UiComponentAlignments.Center,
					Shadow = false,
				},
			],
		},
	};

	private UiModifier Fader() => new()
	{
		Key = "fader-slot",
		Radius = BezelRadius,
		BorderWidth = Hairline,
		BorderColor = BezelColor,
		Background = VerticalGradient("#0a0a0c", "#19191c"),
		Child = new UiStack
		{
			Key = "fader-track",
			Fill = true,
			Padding = UiSize.FromBasis(CapWidth / 2 + 0.01),
			Children =
			[
				new UiLayer
				{
					Key = "fader",
					Fill = true,
					Events =
					[
						UiEventHandler.On(UiComponentEvents.PointerDown, data => FaderDown(data)),
						UiEventHandler.On(UiComponentEvents.PointerMove, data => FaderMove(data)),
					],
					Children = [FaderScale(), FaderGroove(), FaderCap()],
				},
			],
		},
	};

	private static UiStack FaderScale() => new()
	{
		Key = "scale",
		Direction = UiComponentDirections.Horizontal,
		Justify = UiComponentJustify.SpaceBetween,
		Align = UiComponentAlignments.Center,
		Children = Enumerable.Range(0, FaderTicks).Select(i => new UiModifier
		{
			Key = $"tick{i}",
			Frame = new UiFrame
			{
				Width = UiLength.Capped(1.5 / UiLength.Cell, 1.5),
				Height = UiLength.OfParent(i % 5 == 0 ? 0.9 : 0.5, 0.05),
			},
			Child = new UiShape { Key = "mark", Shape = UiComponentShapes.Rectangle, Color = TickColor },
		}).ToArray<UiElement>(),
	};

	private UiStack FaderGroove() => new()
	{
		Key = "groove-row",
		Direction = UiComponentDirections.Horizontal,
		Align = UiComponentAlignments.Center,
		Children =
		[
			new UiModifier
			{
				Key = "groove",
				Fill = true,
				Frame = new UiFrame { Height = UiLength.OfParent(0.12, 0.012) },
				Background = "#040405",
				Clip = UiComponentClips.Capsule,
				Child = new UiTransform
				{
					Key = "groove-level",
					OffsetX = UiValue.From(() => Level - 1),
					Children = [new UiShape { Key = "groove-fill", Shape = UiComponentShapes.Rectangle, Color = AccentColor }],
				},
			},
		],
	};

	private UiTransform FaderCap() => new()
	{
		Key = "cap-position",
		OffsetX = UiValue.From(() => Level - 0.5),
		Children =
		[
			new UiStack
			{
				Key = "cap-center",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Children =
				[
					new UiModifier
					{
						Key = "cap",
						Frame = new UiFrame { Width = UiLength.OfBasis(CapWidth), Height = UiLength.OfParent(1, 0.12) },
						Radius = UiSize.FromBasis(0.012),
						BorderWidth = Hairline,
						BorderColor = "#050506",
						Background = UiBackground.Of(UiGradient.Linear(90,
						[
							new UiGradientStop { Offset = 0, Color = "#2a2a2f" },
							new UiGradientStop { Offset = 0.5, Color = "#48484e" },
							new UiGradientStop { Offset = 1, Color = "#2a2a2f" },
						])),
						Child = new UiStack
						{
							Key = "cap-face",
							Direction = UiComponentDirections.Horizontal,
							Justify = UiComponentJustify.Center,
							Align = UiComponentAlignments.Stretch,
							Children = [new UiShape { Key = "cap-index", MainSize = Hairline, Shape = UiComponentShapes.Rectangle, Color = "#f2f2f2" }],
						},
					},
				],
			},
		],
	};

	private UiStack StepKeys(params int[] deltas) => new()
	{
		Key = deltas[0] < 0 ? "minus-keys" : "plus-keys",
		MainSize = UiSize.FromBasis(StepKeySize),
		Justify = UiComponentJustify.Center,
		Gap = Gap,
		Children = deltas.Select(StepKey).ToArray<UiElement>(),
	};

	private UiModifier StepKey(int delta) => Key(
		(delta < 0 ? "minus" : "plus") + Math.Abs(delta).ToString(CultureInfo.InvariantCulture),
		StepKeySize,
		UiEventHandler.On(UiComponentEvents.Press, () => SetManualTempo(_tempo.Peek() + delta)),
		Label(delta.ToString("+0;−0", CultureInfo.InvariantCulture), LabelColor));

	private UiModifier PlayKey(double? mainSize = null) => Key(
		"play",
		mainSize,
		UiEventHandler.On(UiComponentEvents.Press, TogglePlaying),
		new UiWhen
		{
			Key = "stop-glyph",
			Condition = () => _playing.Value,
			Content = () => new UiModifier
			{
				Key = "stop-frame",
				Frame = new UiFrame { Width = UiLength.OfBasis(0.06), Height = UiLength.OfBasis(0.06) },
				Child = new UiShape
				{
					Key = "stop",
					Shape = UiComponentShapes.RoundedRectangle,
					CornerRadius = UiSize.FromBasis(0.008),
					Color = AccentColor,
				},
			},
		},
		new UiWhen
		{
			Key = "play-glyph",
			Condition = () => !_playing.Value,
			Content = () => new UiIcon { Key = "play", Icon = UiIcons.Play, Size = UiSize.Capped(0.08, 12), Color = LabelColor },
		});


	private UiStack BeatIndicator() => new()
	{
		Key = "beat-slot",
		MainSize = UiSize.FromBasis(0.1),
		Justify = UiComponentJustify.Center,
		Align = UiComponentAlignments.Center,
		Children =
		[
			new UiModifier
			{
				Key = "beat-frame",
				Frame = new UiFrame { Width = UiLength.OfBasis(BeatLightSize), Height = UiLength.OfBasis(BeatLightSize) },
				Clip = UiComponentClips.Circle,
				Child = new UiLayer
				{
					Key = "beat",
					Children =
					[
						new UiShape
						{
							Key = "beat-lens",
							Shape = UiComponentShapes.Circle,
							Color = LedOffColor,
							StrokeColor = BezelColor,
							StrokeWidth = UiSize.FromBasis(0.01),
						},
						new UiWhen
						{
							Key = "beat-lit",
							Condition = () => _beatImage.Value is not null,
							Content = () => new UiImage
							{
								Key = "beat-light",
								Size = UiSize.FromBasis(BeatLightSize),
								Source = UiValue.Optional(() => _beatImage.Value is { } image ? UiValue.Of(image) : UiValue.None<UiResource>()),
							},
						},
					],
				},
			},
		],
	};

	private UiModifier TapKey(double? mainSize = null) => Key(
		"tap",
		mainSize,
		UiEventHandler.On(UiComponentEvents.PressStart, Tap),
		new UiModifier
		{
			Key = "led-frame",
			Frame = new UiFrame { Width = UiLength.OfBasis(0.08), Height = UiLength.OfBasis(0.014) },
			Child = new UiShape
			{
				Key = "led",
				Shape = UiComponentShapes.Capsule,
				Color = UiValue.From(() => _tapping.Value ? AccentColor : LedOffColor),
			},
		},
		Label(Strings.Widgets.Metronome.Tap(), AccentColor));

	private static UiTextRun Label(UiText text, string color) => new()
	{
		Key = "label",
		Text = text,
		Size = UiSize.Capped(0.075, 11),
		Weight = UiComponentTextWeights.Medium,
		Color = color,
		Align = UiComponentAlignments.Center,
		Shadow = false,
	};

	private static UiModifier Key(string key, double? mainSize, UiEventHandler handler, params UiElement[] content) => new()
	{
		Key = $"{key}-bezel",
		MainSize = Extent(mainSize),
		Fill = mainSize is null,
		Padding = BezelWidth,
		Radius = BezelRadius,
		Background = BezelColor,
		Child = new UiModifier
		{
			Key = $"{key}-cap",
			Radius = CapRadius,
			BorderWidth = Hairline,
			BorderColor = KeyEdgeColor,
			Background = VerticalGradient(KeyTopColor, KeyBottomColor),
			Child = new UiButton
			{
				Key = key,
				Background = KeyBottomColor,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = UiSize.FromBasis(0.018),
				Events = [handler],
				Children = content,
			},
		},
	};

	private static UiBackground VerticalGradient(string top, string bottom) => UiBackground.Of(UiGradient.Linear(180,
	[
		new UiGradientStop { Offset = 0, Color = top },
		new UiGradientStop { Offset = 1, Color = bottom },
	]));

	private static UiSize Extent(double? mainSize) => mainSize is { } basis ? UiSize.FromBasis(basis) : UiSize.None();

	private UiEventOutcome FaderDown(UiEventData data)
	{
		if (!data.TryGetPointerDown(out var down) || down.Width <= 0)
		{
			return UiEventOutcome.Rejected("The pointer-down payload is not usable.");
		}

		_faderWidth = down.Width;
		MoveFader(down.Sample.X);
		return UiEventOutcome.Accepted;
	}

	private UiEventOutcome FaderMove(UiEventData data)
	{
		if (!data.TryGetPointerSamples(out var samples) || samples.Count == 0 || _faderWidth <= 0)
		{
			return UiEventOutcome.Rejected("The pointer-move payload is not usable.");
		}

		MoveFader(samples[^1].X);
		return UiEventOutcome.Accepted;
	}

	private void MoveFader(double x)
		=> SetManualTempo((int)Math.Round(MinTempo + Math.Clamp(x / _faderWidth, 0, 1) * (MaxTempo - MinTempo)));

	private void Tap()
	{
		var now = Stopwatch.GetTimestamp();
		if (_taps.Count > 0 && Stopwatch.GetElapsedTime(_taps[^1], now) > TapTimeout)
		{
			_taps.Clear();
		}

		if (_taps.Count == MaxTapsAveraged)
		{
			_taps.RemoveAt(0);
		}

		_taps.Add(now);
		_tapping.Value = true;
		if (_taps.Count < 2)
		{
			return;
		}

		var beat = Stopwatch.GetElapsedTime(_taps[0], now) / (_taps.Count - 1);
		SetTempo((int)Math.Round(60 / beat.TotalSeconds));
	}

	private void TogglePlaying()
	{
		if (_playing.Peek())
		{
			StopTicker();
			HideBeatImage();
			_playing.Value = false;
			return;
		}

		var ticker = new CancellationTokenSource();
		Interlocked.Exchange(ref _ticker, ticker)?.Cancel();
		_playing.Value = true;
		ShowBeatImage(settle: false);
		_ = ClickAsync(ticker);
	}

	private void ShowBeatImage(bool settle)
	{
		if (_beatImages is not null)
		{
			_ = LoadBeatImageAsync(_tempo.Peek(), settle, Interlocked.Increment(ref _beatImageVersion));
		}
	}

	private async Task LoadBeatImageAsync(int tempo, bool settle, int version)
	{
		if (settle)
		{
			await Task.Delay(TempoSettleDelay);
		}

		if (version != Volatile.Read(ref _beatImageVersion))
		{
			return;
		}

		var image = await _beatImages!(tempo);
		lock (_beatImageGate)
		{
			if (version == _beatImageVersion)
			{
				_beatImage.Value = image;
			}
		}
	}

	private void HideBeatImage()
	{
		lock (_beatImageGate)
		{
			Interlocked.Increment(ref _beatImageVersion);
			_beatImage.Value = null;
		}
	}

	private async Task ClickAsync(CancellationTokenSource ticker)
	{
		try
		{
			using var audio = _clickPlayer is null ? null : await Task.Run(_clickPlayer.Acquire, ticker.Token);
			var nextBeat = Stopwatch.GetTimestamp();
			while (true)
			{
				await WaitUntilAsync(nextBeat, ticker.Token);
				_clickPlayer?.Play(_sound);
				nextBeat += (long)(Stopwatch.Frequency * 60.0 / _tempo.Peek());
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			ticker.Dispose();
		}
	}

	// Task.Delay is only as fine as the OS timer (about 15 ms on Windows)
	private static async Task WaitUntilAsync(long deadline, CancellationToken token)
	{
		var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
		if (remaining > SpinWindow)
		{
			await Task.Delay(remaining - SpinWindow, token);
		}

		while (Stopwatch.GetTimestamp() < deadline)
		{
			token.ThrowIfCancellationRequested();
			Thread.Yield();
		}
	}

	private void StopTicker() => Interlocked.Exchange(ref _ticker, null)?.Cancel();

	public void Dispose()
	{
		StopTicker();
		Interlocked.Increment(ref _beatImageVersion);
	}

	private void SetManualTempo(int tempo)
	{
		_taps.Clear();
		_tapping.Value = false;
		SetTempo(tempo);
	}

	private void SetTempo(int tempo)
	{
		_tempo.Value = Math.Clamp(tempo, MinTempo, MaxTempo);
		_tempoChanged(_tempo.Peek());
		if (_playing.Peek())
		{
			ShowBeatImage(settle: true);
		}
	}
}
