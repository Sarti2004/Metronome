using System.Text.Json;
using MacroDeck.Localization;

namespace Metronome;

public sealed record ClickSound(string Id, Func<LocalizedString> Label, double Seconds, Func<double, double> Wave);

public static class ClickSounds
{
	public const string DataKey = "sound";
	public const string Default = "click";

//  Sounds library
	public static IReadOnlyList<ClickSound> All { get; } =
	[
		new("click", Strings.Widgets.Metronome.Sounds.Click, 0.025, t => Decay(t, 0.004) * Sine(1500, t)),
		new("woodblock", Strings.Widgets.Metronome.Sounds.Woodblock, 0.08, t => Decay(t, 0.012) * ((0.7 * Sine(900, t)) + (0.3 * Sine(2400, t)))),
		new("clave", Strings.Widgets.Metronome.Sounds.Clave, 0.06, t => Decay(t, 0.015) * Sine(2500, t)),
		new("beep", Strings.Widgets.Metronome.Sounds.Beep, 0.06, t => Ramp(t, 0.06) * Sine(1000, t)),
		new("cowbell", Strings.Widgets.Metronome.Sounds.Cowbell, 0.2, t => Decay(t, 0.05) * 0.4 * (Square(540, t) + Square(800, t))),
		new("hihat", Strings.Widgets.Metronome.Sounds.HiHat, 0.06, t => Decay(t, 0.01) * Noise(t)),
	];

	public static string Read(JsonElement data)
	{
		var id = data.ValueKind == JsonValueKind.Object && data.TryGetProperty(DataKey, out var value)
			&& value.ValueKind == JsonValueKind.String ? value.GetString() : null;
		return All.Any(sound => sound.Id == id) ? id! : Default;
	}

	public static string DataSchema { get; } = JsonSerializer.Serialize(new
	{
		type = "object",
		properties = new Dictionary<string, object>
		{
			[DataKey] = new { type = "string", @enum = All.Select(sound => sound.Id) },
		},
	});

	public static short[] Synthesize(ClickSound sound, int sampleRate)
	{
		var samples = new short[(int)(sound.Seconds * sampleRate)];
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = (short)(short.MaxValue * 0.8 * Math.Clamp(sound.Wave((double)i / sampleRate), -1, 1));
		}

		return samples;
	}

	private static double Sine(double hertz, double t) => Math.Sin(2 * Math.PI * hertz * t);

	private static double Square(double hertz, double t) => Math.Sign(Sine(hertz, t));

	private static double Decay(double t, double timeConstant) => Math.Exp(-t / timeConstant);

	// Short fades at both ends keep a plain tone from popping.
	private static double Ramp(double t, double length) => Math.Min(1, Math.Min(t, length - t) / 0.003);

	// A deterministic hash of the time, so the hi-hat sounds the same on every machine and run.
	private static double Noise(double t)
	{
		var x = Math.Sin(t * 1e7) * 43758.5453;
		return (2 * (x - Math.Floor(x))) - 1;
	}
}
