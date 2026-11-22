using OpenTK.Audio.OpenAL;
using NAudio.Wave;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;

public class AudioManager : IDisposable
{
    private readonly ALDevice _device;
    private readonly ALContext _context;
    private readonly bool _isAvailable;

    private readonly Dictionary<string, int> _buffers = new();
    private readonly List<int> _sources = new();

    public AudioManager()
    {
        try
        {
            _device = ALC.OpenDevice(null);
            _context = ALC.CreateContext(_device, (int[])null!);

            ALC.MakeContextCurrent(_context);
            _isAvailable = true;
        }
        catch (DllNotFoundException)
        {
            _device = default;
            _context = default;
            _isAvailable = false;

            Console.Error.WriteLine(
                "OpenAL was not found. Audio is disabled for this session.");
        }
        catch (EntryPointNotFoundException)
        {
            _device = default;
            _context = default;
            _isAvailable = false;

            Console.Error.WriteLine(
                "The installed OpenAL runtime is incompatible. Audio is disabled for this session.");
        }
    }

    public void Load(string name, string path)
    {
        if (!_isAvailable)
            return;

        using var reader = new WaveFileReader(path);

        ALFormat format = (reader.WaveFormat.Channels, reader.WaveFormat.BitsPerSample) switch
        {
            (1, 8) => ALFormat.Mono8,
            (1, 16) => ALFormat.Mono16,
            (2, 8) => ALFormat.Stereo8,
            (2, 16) => ALFormat.Stereo16,
            _ => throw new NotSupportedException("Unsupported WAV format.")
        };

        if (reader.Length > int.MaxValue)
            throw new NotSupportedException("Audio files larger than 2 GB are not supported.");

        byte[] data = new byte[(int)reader.Length];
        reader.ReadExactly(data);

        int buffer = AL.GenBuffer();

        GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            AL.BufferData(
                buffer,
                format,
                dataHandle.AddrOfPinnedObject(),
                data.Length,
                reader.WaveFormat.SampleRate
            );
        }
        finally
        {
            dataHandle.Free();
        }

        _buffers[name] = buffer;
    }

    public int Play(string name, float volume = 1f, bool loop = false)
    {
        if (!_isAvailable || !_buffers.ContainsKey(name))
            return 0;

        int source = AL.GenSource();

        AL.Source(source, ALSourcei.Buffer, _buffers[name]);
        AL.Source(source, ALSourcef.Gain, volume);
        AL.Source(source, ALSourceb.Looping, loop);

        AL.SourcePlay(source);

        _sources.Add(source);

        return source;
    }

    public int PlayAt(string name, Vector3 position, float volume = 1f)
    {
        int source = Play(name, volume);

        if (source == 0)
            return 0;

        AL.Source(
            source,
            ALSource3f.Position,
            position.X,
            position.Y,
            position.Z
        );

        return source;
    }

    public void SetListener(Vector3 position)
    {
        if (!_isAvailable)
            return;

        AL.Listener(
            ALListener3f.Position,
            position.X,
            position.Y,
            position.Z
        );
    }

    public void Update()
    {
        if (!_isAvailable)
            return;

        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            int source = _sources[i];

            AL.GetSource(source, ALGetSourcei.SourceState, out int state);

            if ((ALSourceState)state != ALSourceState.Stopped)
                continue;

            AL.DeleteSource(source);
            _sources.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        if (!_isAvailable)
            return;

        foreach (int source in _sources)
            AL.DeleteSource(source);

        foreach (int buffer in _buffers.Values)
            AL.DeleteBuffer(buffer);

        ALC.MakeContextCurrent(ALContext.Null);
        ALC.DestroyContext(_context);
        ALC.CloseDevice(_device);
    }
}
