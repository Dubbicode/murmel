using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SharpCompress.Readers;

namespace Murmel.Services;

/// <summary>
/// Downloads and unpacks the Parakeet v3 ONNX model on first launch.
///
/// The model itself is ~640 MB, far too big to ship inside the app or transfer
/// through small-file channels - so instead the app fetches it once, directly
/// from the official sherpa-onnx release, straight to the user's machine.
/// After this one-time download, everything runs 100% locally / offline -
/// no audio or text ever leaves the machine.
/// </summary>
public class ModelManager
{
    private const string ModelUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2";

    public string ModelDir { get; }
    public string EncoderPath => Path.Combine(ModelDir, "encoder.int8.onnx");
    public string DecoderPath => Path.Combine(ModelDir, "decoder.int8.onnx");
    public string JoinerPath => Path.Combine(ModelDir, "joiner.int8.onnx");
    public string TokensPath => Path.Combine(ModelDir, "tokens.txt");

    public ModelManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        ModelDir = Path.Combine(appData, "Murmel", "Models", "parakeet-tdt-v3");
        Directory.CreateDirectory(ModelDir);
    }

    public bool IsModelReady() =>
        File.Exists(EncoderPath) && File.Exists(DecoderPath) &&
        File.Exists(JoinerPath) && File.Exists(TokensPath);

    /// <summary>
    /// Downloads the model archive (reporting 0-90% progress) and extracts it
    /// directly into <see cref="ModelDir"/> (reporting 90-100%), flattening the
    /// single top-level folder inside the archive.
    /// </summary>
    public async Task EnsureModelDownloadedAsync(IProgress<(double percent, string status)> progress)
    {
        if (IsModelReady())
        {
            progress.Report((100, "Modell bereit"));
            return;
        }

        var archivePath = Path.Combine(Path.GetTempPath(), "murmel-parakeet-v3.tar.bz2");

        using (var http = new HttpClient())
        {
            http.Timeout = TimeSpan.FromMinutes(30);
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 512_000_000L;
            await using var httpStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(archivePath);

            var buffer = new byte[81920];
            long readSoFar = 0;
            int bytesRead;
            while ((bytesRead = await httpStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                readSoFar += bytesRead;
                var pct = Math.Min(90.0, readSoFar * 90.0 / totalBytes);
                var mbDone = readSoFar / 1_000_000;
                var mbTotal = totalBytes / 1_000_000;
                progress.Report((pct, $"Modell wird heruntergeladen... {mbDone} / {mbTotal} MB"));
            }
        }

        progress.Report((92, "Modell wird entpackt..."));

        await Task.Run(() =>
        {
            using var stream = File.OpenRead(archivePath);
            using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions());
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory) continue;

                // Flatten: the archive contains one top-level folder; we only
                // want the files directly, named as they appear (encoder.int8.onnx etc.)
                var fileName = Path.GetFileName(reader.Entry.Key ?? string.Empty);
                if (string.IsNullOrEmpty(fileName)) continue;

                // Skip the bundled test_wavs/ samples - not needed at runtime.
                if ((reader.Entry.Key ?? string.Empty).Contains("test_wavs")) continue;

                var destPath = Path.Combine(ModelDir, fileName);
                reader.WriteEntryToFile(destPath);
            }
        });

        File.Delete(archivePath);
        progress.Report((100, "Modell bereit"));
    }
}
