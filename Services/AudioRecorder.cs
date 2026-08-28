using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Murmel.Services;

/// <summary>
/// Captures microphone audio while the hotkey is held, directly at 16kHz mono
/// 16-bit PCM (what Parakeet expects) so no separate resampling step is needed.
/// </summary>
public class AudioRecorder
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;

    public bool IsRecording { get; private set; }

    /// <summary>Fires roughly every ~50ms while recording with the current input
    /// level (0..1, RMS-based) - used to drive the live audio-level widget.</summary>
    public event Action<float>? LevelChanged;

    public void Start()
    {
        if (IsRecording) return;

        _buffer = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(ParakeetTranscriber.SampleRate, 16, 1),
            BufferMilliseconds = 50
        };
        _waveIn.DataAvailable += (_, e) =>
        {
            _buffer.Write(e.Buffer, 0, e.BytesRecorded);
            LevelChanged?.Invoke(ComputeLevel(e.Buffer, e.BytesRecorded));
        };

        _waveIn.StartRecording();
        IsRecording = true;
    }

    /// <summary>RMS amplitude of a 16-bit PCM chunk, normalized to roughly 0..1
    /// (with a gain boost since normal speech RMS is usually fairly quiet).</summary>
    private static float ComputeLevel(byte[] buffer, int bytesRecorded)
    {
        int sampleCount = bytesRecorded / 2;
        if (sampleCount == 0) return 0f;

        double sumSquares = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        return (float)Math.Clamp(rms * 6.0, 0.0, 1.0); // gain boost so normal speech visibly moves the bars
    }

    /// <summary>Stops recording and returns the captured audio as normalized float32 samples.</summary>
    public async Task<float[]> StopAsync()
    {
        if (!IsRecording || _waveIn is null || _buffer is null) return Array.Empty<float>();

        var tcs = new TaskCompletionSource();
        _waveIn.RecordingStopped += (_, _) => tcs.TrySetResult();
        _waveIn.StopRecording();
        await tcs.Task;

        IsRecording = false;

        var pcmBytes = _buffer.ToArray();
        _waveIn.Dispose();
        _waveIn = null;
        _buffer.Dispose();
        _buffer = null;

        return PcmBytesToFloat(pcmBytes);
    }

    private static float[] PcmBytesToFloat(byte[] pcm)
    {
        int sampleCount = pcm.Length / 2; // 16-bit = 2 bytes per sample
        var result = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            result[i] = sample / 32768f;
        }
        return result;
    }
}
