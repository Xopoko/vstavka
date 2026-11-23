using System;
using System.IO;
using System.Media;

namespace Vstavka.Services;

internal sealed class SoundService : IDisposable
{
    private SoundPlayer? copy,paste;
    private MemoryStream? copyStream,pasteStream;
    internal void SetVolume(double volume)
    {
        Dispose();
        copyStream=new MemoryStream(CreateWave(false,volume));pasteStream=new MemoryStream(CreateWave(true,volume));
        copy=new SoundPlayer(copyStream);paste=new SoundPlayer(pasteStream);
        try { copy.Load();paste.Load(); }catch { }
    }
    internal void Copy() {try {copy?.Play();}catch { }}
    internal void Paste() {try {paste?.Play();}catch { }}
    internal static byte[] CreateWave(bool paste,double volume)
    {
        const int rate=44100;
        var length=(int)(rate*(paste?0.055:0.105));
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+length*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(length*2);
        for(var i=0;i<length;i++)
        {
            double t=(double)i/rate,progress=(double)i/length;
            var frequency=paste?1450:880;
            var envelope=Math.Sin(Math.PI*Math.Min(1,t/0.004)/2)*Math.Exp(-progress*(paste?9:6))*Math.Pow(1-progress,2);
            var value=(Math.Sin(2*Math.PI*frequency*t)+0.28*Math.Sin(2*Math.PI*(frequency*1.5)*t))*envelope;
            writer.Write((short)(Math.Clamp(volume,0,1)*14000*value));
        }
        return stream.ToArray();
    }
    public void Dispose() {copy?.Dispose();paste?.Dispose();copyStream?.Dispose();pasteStream?.Dispose();}
}
