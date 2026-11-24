using System.ComponentModel;
using System.Media;
using System.Runtime.Versioning;

namespace Vstavka.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class SoundService : IDisposable
{
    private SoundPlayer? copy;
    private SoundPlayer? paste;
    private double volume;

    internal void Play(bool pasteSound, double newVolume)
    {
        if (newVolume <= 0)
            return;
        if (volume != newVolume)
        {
            Dispose();
            volume = newVolume;
        }

        ref var player = ref (pasteSound ? ref paste : ref copy);
        player ??= new SoundPlayer(new MemoryStream(CreateWave(pasteSound, volume), writable: false));
        try { player.Play(); }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or Win32Exception)
        {
            // A missing audio device must not interrupt clipboard actions.
        }
    }

    internal static byte[] CreateWave(bool paste, double volume)
    {
        const int sampleRate = 44100;
        const int bytesPerSample = sizeof(short);
        var samples = (int)(sampleRate * (paste ? 0.055 : 0.105));
        var dataBytes = samples * bytesPerSample;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // Mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * bytesPerSample);
        writer.Write((short)bytesPerSample);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        var frequency = paste ? 1450 : 880;
        var decay = paste ? 9 : 6;
        var amplitude = Math.Clamp(volume, 0, 1) * 14000;
        for (var index = 0; index < samples; index++)
        {
            var seconds = (double)index / sampleRate;
            var progress = (double)index / samples;
            var phase = 2 * Math.PI * frequency * seconds;
            var envelope = Math.Sin(Math.PI * Math.Min(1, seconds / 0.004) / 2) *
                Math.Exp(-progress * decay) * Math.Pow(1 - progress, 2);
            writer.Write((short)(amplitude * envelope * (Math.Sin(phase) + 0.28 * Math.Sin(phase * 1.5))));
        }
        return stream.ToArray();
    }

    public void Dispose()
    {
        DisposePlayer(copy);
        DisposePlayer(paste);
        copy = paste = null;
    }

    private static void DisposePlayer(SoundPlayer? player)
    {
        using var stream = player?.Stream;
        player?.Dispose();
    }
}
