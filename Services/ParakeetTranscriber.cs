using SherpaOnnx;

namespace Murmel.Services;

/// <summary>
/// Wraps sherpa-onnx's OfflineRecognizer configured for the NVIDIA Parakeet
/// TDT v3 model. Everything here runs locally via ONNX Runtime - no network
/// calls, no cloud - once the model files are on disk (see ModelManager).
/// </summary>
public class ParakeetTranscriber
{
    private readonly OfflineRecognizer _recognizer;
    public const int SampleRate = 16000;

    public ParakeetTranscriber(ModelManager models)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80;

        config.ModelConfig.Tokens = models.TokensPath;
        config.ModelConfig.Transducer.Encoder = models.EncoderPath;
        config.ModelConfig.Transducer.Decoder = models.DecoderPath;
        config.ModelConfig.Transducer.Joiner = models.JoinerPath;
        // Parakeet TDT needs this specific model type so sherpa-onnx uses
        // TDT decoding (duration-aware) rather than plain RNN-T greedy search.
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = 4;
        config.ModelConfig.Debug = 0;

        config.DecodingMethod = "greedy_search";

        _recognizer = new OfflineRecognizer(config);
    }

    /// <summary>Transcribes a mono float32 PCM buffer (expects <see cref="SampleRate"/> Hz).</summary>
    public string Transcribe(float[] samples)
    {
        if (samples.Length == 0) return string.Empty;

        var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(SampleRate, samples);
        _recognizer.Decode(stream);
        return stream.Result.Text?.Trim() ?? string.Empty;
    }
}
