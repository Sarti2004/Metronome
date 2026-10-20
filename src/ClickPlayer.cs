using Serilog;
using Silk.NET.OpenAL;

namespace Metronome;


/// Plays the metronome click on computer
public sealed class ClickPlayer : IDisposable
{
	private const int SampleRate = 48000;
	
	private const int MixRefreshHz = 250;

	private static readonly TimeSpan AuditionLength = TimeSpan.FromMilliseconds(400);
	private static readonly Dictionary<string, short[]> Samples =
		ClickSounds.All.ToDictionary(sound => sound.Id, sound => ClickSounds.Synthesize(sound, SampleRate));

	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private ALContext? _alc;
	private AL? _al;
	private unsafe Device* _device;
	private unsafe Context* _context;
	private readonly Dictionary<string, (uint Buffer, uint Source)> _voices = [];
	private int _users;

	public ClickPlayer(ILogger logger) => _logger = logger.ForContext<ClickPlayer>();
	
	public IDisposable Acquire()
	{
		lock (_gate)
		{
			_users++;
			if (_al is null)
			{
				TryOpen();
			}
		}

		return new Lease(this);
	}

	public void Play(string sound)
	{
		lock (_gate)
		{
			if (_al is not null && _voices.TryGetValue(sound, out var voice))
			{
				_al.SourcePlay(voice.Source);
			}
		}
	}

	public async Task AuditionAsync(string sound)
	{
		using var lease = await Task.Run(Acquire);
		Play(sound);
		await Task.Delay(AuditionLength);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			Close();
			_users = 0;
		}
	}

	private void Release()
	{
		lock (_gate)
		{
			if (_users > 0 && --_users == 0)
			{
				Close();
			}
		}
	}

	// retry if was not able to open the device
	private unsafe void TryOpen()
	{
		try
		{
			_alc = ALContext.GetApi(soft: true);
			_al = AL.GetApi(soft: true);
			_device = _alc.OpenDevice(null!);
			if (_device == null)
			{
				throw new InvalidOperationException("No audio output device is available.");
			}

			var attributes = stackalloc int[] { (int)ContextAttributes.Refresh, MixRefreshHz, 0 };
			_context = _alc.CreateContext(_device, attributes);
			_alc.MakeContextCurrent(_context);
			foreach (var (id, samples) in Samples)
			{
				var buffer = _al.GenBuffer();
				_al.BufferData(buffer, BufferFormat.Mono16, samples, SampleRate);
				var source = _al.GenSource();
				_al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
				_voices[id] = (buffer, source);
			}
		}
#pragma warning disable CA1031 // A missing native library or audio device surfaces as many exception types; the metronome must keep running silently.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_logger.Warning(ex, "Audio output is unavailable");
			Close();
		}
	}

	private unsafe void Close()
	{
		if (_al is not null)
		{
			foreach (var (buffer, source) in _voices.Values)
			{
				_al.DeleteSource(source);
				_al.DeleteBuffer(buffer);
			}
		}

		_voices.Clear();

		if (_alc is not null)
		{
			if (_context != null)
			{
				_alc.MakeContextCurrent(null);
				_alc.DestroyContext(_context);
			}

			if (_device != null)
			{
				_alc.CloseDevice(_device);
			}
		}

		_al?.Dispose();
		_alc?.Dispose();
		_al = null;
		_alc = null;
		_device = null;
		_context = null;
	}

	private sealed class Lease(ClickPlayer owner) : IDisposable
	{
		private int _released;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				owner.Release();
			}
		}
	}
}
