using System.Collections.Concurrent;
using System.Numerics;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using Serilog;

namespace Metronome;

// blinking led, just workaround, doesn't blink reliably otherwise.
public sealed class ImageGenerator
{
	private const int CentisecondsPerMinute = 6000;
	private const double LitFraction = 0.4;

	private static readonly byte[] LitColor = Convert.FromHexString(MetronomeWidget.AccentColor.AsSpan(1));
	private static readonly byte[] DarkColor = Convert.FromHexString(MetronomeWidget.LedOffColor.AsSpan(1));

	private readonly ConcurrentDictionary<int, Task<UiResource?>> _images = new();
	private readonly ILogger _logger;
	private volatile IUiResourceRegistry? _registry;

	public ImageGenerator(ILogger logger) => _logger = logger.ForContext<ImageGenerator>();

	// Registered resources do not outlive the host session, so a new session starts with an empty cache.
	public void Attach(IUiResourceRegistry registry)
	{
		_registry = registry;
		_images.Clear();
	}

	public Task<UiResource?> GetAsync(int tempo) => _images.GetOrAdd(tempo, RegisterAsync);

	private async Task<UiResource?> RegisterAsync(int tempo)
	{
		if (_registry is not { } registry)
		{
			_images.TryRemove(tempo, out _);
			return null;
		}

		try
		{
			return await registry.RegisterAsync($"beat-{tempo}", Encode(tempo), "image/gif", CancellationToken.None);
		}
		catch (Exception ex) when (ex is UiResourceException or HostInvocationException)
		{
			_images.TryRemove(tempo, out _);
			return null;
		}
	}

	private static byte[] Encode(int tempo)
	{
		var beats = tempo / (int)BigInteger.GreatestCommonDivisor(tempo, CentisecondsPerMinute);
		using var gif = new MemoryStream();
		gif.Write("GIF89a"u8);
		gif.Write([1, 0, 1, 0, 0x80, 0, 0]);
		gif.Write(LitColor);
		gif.Write(DarkColor);
		gif.Write([0x21, 0xFF, 0x0B]);
		gif.Write("NETSCAPE2.0"u8);
		gif.Write([3, 1, 0, 0, 0]);

		var beatStart = 0;
		for (var beat = 1; beat <= beats; beat++)
		{
			var nextBeatStart = (int)Math.Round((double)beat * CentisecondsPerMinute / tempo);
			var lit = (int)Math.Round((nextBeatStart - beatStart) * LitFraction);
			WriteFrame(gif, 0, lit);
			WriteFrame(gif, 1, nextBeatStart - beatStart - lit);
			beatStart = nextBeatStart;
		}

		gif.WriteByte(0x3B);
		return gif.ToArray();
	}

	// One 1x1 frame of a single palette color: LZW codes clear (4), the pixel and end (5), three bits each.
	private static void WriteFrame(MemoryStream gif, byte colorIndex, int delayCentiseconds)
	{
		gif.Write([0x21, 0xF9, 4, 0, (byte)delayCentiseconds, (byte)(delayCentiseconds >> 8), 0, 0]);
		gif.Write([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
		var codes = 4 | (colorIndex << 3) | (5 << 6);
		gif.Write([2, 2, (byte)codes, (byte)(codes >> 8), 0]);
	}
}
