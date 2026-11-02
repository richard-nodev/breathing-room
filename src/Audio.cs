using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BreathingRoom
{
    // A tiny software mixer on top of waveOut, so several sounds (a purr, a bounce, a mew) can
    // overlap and fade smoothly. The device is opened on demand and closed after a few seconds of
    // silence, so an idle app never holds the audio device or keeps the PC awake.
    public static class Audio
    {
        public const int Rate = 44100;
        const int Blocks = 4, Frames = 1024;
        const uint WAVE_MAPPER = 0xFFFFFFFF, CALLBACK_EVENT = 0x50000, WHDR_DONE = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, uint device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);

        class Voice
        {
            public float[] Data;
            public int Pos;
            public float Vol, Target, Step;
            public bool Loop, StopAtZero;
            public string Tag;
        }

        static readonly List<Voice> voices = new List<Voice>();
        static readonly object gate = new object();
        static bool running;
        static volatile float master = 0.7f;

        public static float Master
        {
            get { return master; }
            set { master = Math.Max(0f, Math.Min(1f, value)); }
        }

        public static void Play(float[] clip, float volume)
        {
            if (clip == null || volume <= 0) return;
            lock (gate)
            {
                if (voices.Count > 24) return;
                voices.Add(new Voice { Data = clip, Vol = volume, Target = volume });
            }
            EnsureRunning();
        }

        // Starts a looping sound (or retargets the running one) and fades it to the given volume.
        public static void Loop(string tag, float[] clip, float volume, double fadeSeconds)
        {
            if (clip == null) return;
            lock (gate)
            {
                Voice v = voices.Find(x => x.Tag == tag);
                if (v == null)
                {
                    v = new Voice { Data = clip, Loop = true, Tag = tag, Vol = 0 };
                    voices.Add(v);
                }
                v.StopAtZero = false;
                v.Target = volume;
                v.Step = (float)(Math.Abs(volume - v.Vol) / Math.Max(1, fadeSeconds * Rate));
            }
            EnsureRunning();
        }

        public static void StopLoop(string tag, double fadeSeconds)
        {
            lock (gate)
            {
                foreach (Voice v in voices)
                {
                    if (v.Tag != tag) continue;
                    v.Target = 0;
                    v.StopAtZero = true;
                    v.Step = (float)(v.Vol / Math.Max(1, fadeSeconds * Rate));
                }
            }
        }

        public static bool IsLooping(string tag)
        {
            lock (gate) return voices.Exists(v => v.Tag == tag && !v.StopAtZero);
        }

        static void EnsureRunning()
        {
            lock (gate)
            {
                if (running) return;
                running = true;
            }
            var t = new Thread(Run) { IsBackground = true, Name = "Breathing Room audio" };
            t.Start();
        }

        static void Run()
        {
            IntPtr device = IntPtr.Zero;
            var ready = new AutoResetEvent(false);
            var headers = new IntPtr[Blocks];
            var buffers = new IntPtr[Blocks];
            int headerSize = Marshal.SizeOf(typeof(WAVEHDR));
            int flagsOffset = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
            var mix = new float[Frames];
            var pcm = new short[Frames];
            try
            {
                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = 1,
                    nChannels = 1,
                    nSamplesPerSec = Rate,
                    nAvgBytesPerSec = Rate * 2,
                    nBlockAlign = 2,
                    wBitsPerSample = 16
                };
                int err = waveOutOpen(out device, WAVE_MAPPER, ref fmt, ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
                if (err != 0)
                {
                    Store.Log("waveOutOpen failed: " + err);
                    lock (gate) { voices.Clear(); running = false; }
                    return;
                }
                for (int i = 0; i < Blocks; i++)
                {
                    buffers[i] = Marshal.AllocHGlobal(Frames * 2);
                    headers[i] = Marshal.AllocHGlobal(headerSize);
                    Marshal.StructureToPtr(new WAVEHDR { lpData = buffers[i], dwBufferLength = Frames * 2 }, headers[i], false);
                    waveOutPrepareHeader(device, headers[i], headerSize);
                    Fill(mix, pcm, buffers[i]);
                    waveOutWrite(device, headers[i], headerSize);
                }

                int quietBlocks = 0;
                int lastProgress = Environment.TickCount;
                bool failed = false;
                while (!failed)
                {
                    ready.WaitOne(100);
                    for (int i = 0; i < Blocks; i++)
                    {
                        if ((Marshal.ReadInt32(headers[i], flagsOffset) & WHDR_DONE) == 0) continue;
                        lastProgress = Environment.TickCount;
                        bool sound = Fill(mix, pcm, buffers[i]);
                        quietBlocks = sound ? 0 : quietBlocks + 1;
                        int writeError = waveOutWrite(device, headers[i], headerSize);
                        if (writeError != 0) { Store.Log("waveOutWrite failed: " + writeError); failed = true; break; }
                    }
                    // A headset that disconnects mid-sound can stop handing buffers back; give up and
                    // let the next sound open the device afresh.
                    if (!failed && Environment.TickCount - lastProgress > 2000)
                    {
                        Store.Log("Audio device stopped responding");
                        failed = true;
                    }
                    if (failed)
                    {
                        lock (gate) { voices.Clear(); running = false; }
                        break;
                    }
                    if (quietBlocks > Rate * 3 / Frames)
                    {
                        lock (gate)
                        {
                            if (voices.Count == 0) { running = false; break; }
                        }
                        quietBlocks = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Store.Log("Audio thread failed: " + ex);
                lock (gate) { voices.Clear(); running = false; }
            }
            finally
            {
                if (device != IntPtr.Zero)
                {
                    waveOutReset(device);
                    for (int i = 0; i < Blocks; i++)
                        if (headers[i] != IntPtr.Zero) waveOutUnprepareHeader(device, headers[i], headerSize);
                    waveOutClose(device);
                }
                for (int i = 0; i < Blocks; i++)
                {
                    if (headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(headers[i]);
                    if (buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(buffers[i]);
                }
                ready.Dispose();
            }
        }

        // Mixes all voices into one block. Returns false when the block is silent.
        static bool Fill(float[] mix, short[] pcm, IntPtr target)
        {
            Array.Clear(mix, 0, mix.Length);
            bool any = false;
            lock (gate)
            {
                for (int n = voices.Count - 1; n >= 0; n--)
                {
                    Voice v = voices[n];
                    bool finished = false;
                    for (int i = 0; i < Frames; i++)
                    {
                        if (v.Vol != v.Target)
                        {
                            if (v.Vol < v.Target) v.Vol = Math.Min(v.Target, v.Vol + v.Step);
                            else v.Vol = Math.Max(v.Target, v.Vol - v.Step);
                        }
                        if (v.Pos >= v.Data.Length)
                        {
                            if (v.Loop) v.Pos = 0;
                            else { finished = true; break; }
                        }
                        mix[i] += v.Data[v.Pos++] * v.Vol;
                    }
                    if (v.StopAtZero && v.Vol <= 0.0001f) finished = true;
                    if (finished) voices.RemoveAt(n);
                    else any = true;
                }
            }
            float g = master;
            for (int i = 0; i < Frames; i++)
            {
                float s = mix[i] * g;
                if (s > 0.85f) s = 0.85f + (s - 0.85f) / (1 + (s - 0.85f) * 6);
                else if (s < -0.85f) s = -0.85f + (s + 0.85f) / (1 - (s + 0.85f) * 6);
                pcm[i] = (short)(Math.Max(-1f, Math.Min(1f, s)) * 32000);
            }
            Marshal.Copy(pcm, 0, target, Frames);
            return any;
        }

        public static float[] LoadWav(string path)
        {
            try
            {
                using (Stream s = File.OpenRead(path)) return LoadWav(s, path);
            }
            catch (Exception ex)
            {
                Store.Log("Could not open sound " + path + ": " + ex.Message);
                return null;
            }
        }

        // Reads a PCM WAV (8/16-bit, mono or stereo, any rate) into mono floats at our rate.
        public static float[] LoadWav(Stream stream, string name)
        {
            string path = name;
            try
            {
                using (var r = new BinaryReader(stream, System.Text.Encoding.ASCII, true))
                {
                    if (new string(r.ReadChars(4)) != "RIFF") return null;
                    r.ReadInt32();
                    if (new string(r.ReadChars(4)) != "WAVE") return null;
                    int channels = 1, rate = Rate, bits = 16;
                    byte[] data = null;
                    while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
                    {
                        string id = new string(r.ReadChars(4));
                        int size = r.ReadInt32();
                        if (id == "fmt ")
                        {
                            int format = r.ReadInt16();
                            channels = r.ReadInt16();
                            rate = r.ReadInt32();
                            r.ReadInt32();
                            r.ReadInt16();
                            bits = r.ReadInt16();
                            if (size > 16) r.ReadBytes(size - 16);
                            if (format != 1 && format != -2) return null;
                        }
                        else if (id == "data")
                        {
                            data = r.ReadBytes(size);
                            break;
                        }
                        else
                        {
                            r.ReadBytes(size + (size & 1));
                        }
                    }
                    if (data == null || (bits != 16 && bits != 8) || channels < 1) return null;
                    int bytesPerSample = bits / 8;
                    int frames = data.Length / (bytesPerSample * channels);
                    var mono = new float[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; c++)
                        {
                            int o = (f * channels + c) * bytesPerSample;
                            sum += bits == 16 ? BitConverter.ToInt16(data, o) / 32768f : (data[o] - 128) / 128f;
                        }
                        mono[f] = sum / channels;
                    }
                    if (rate == Rate) return mono;
                    int outLen = (int)((long)frames * Rate / rate);
                    var resampled = new float[outLen];
                    for (int i = 0; i < outLen; i++)
                    {
                        double src = (double)i * rate / Rate;
                        int a = (int)src;
                        int b = Math.Min(frames - 1, a + 1);
                        double t = src - a;
                        resampled[i] = (float)(mono[Math.Min(a, frames - 1)] * (1 - t) + mono[b] * t);
                    }
                    return resampled;
                }
            }
            catch (Exception ex)
            {
                Store.Log("Could not read sound " + path + ": " + ex.Message);
                return null;
            }
        }
    }
}
