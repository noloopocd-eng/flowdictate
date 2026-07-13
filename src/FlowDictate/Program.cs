using System.IO;
using FlowDictate.Cleanup;
using FlowDictate.Core;
using FlowDictate.Transcription;
using NAudio.Wave;

namespace FlowDictate;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--selftest")
        {
            // Headless pipeline test: wav in -> transcript + cleaned text out to a report file.
            SelfTest(wavPath: args.Length > 1 ? args[1] : throw new ArgumentException("--selftest <wav> <report> [selectionFile]"),
                     reportPath: args.Length > 2 ? args[2] : "selftest_report.txt",
                     selectionPath: args.Length > 3 ? args[3] : null)
                .GetAwaiter().GetResult();
            return;
        }

        if (args.Length >= 2 && args[0] == "--testinsert")
        {
            // Insertion test: wait for the caller to focus a target window, then insert.
            Thread.Sleep(1500);
            string strategy = Insertion.TextInserter.InsertAtCursor(args[1]);
            Thread.Sleep(500); // let paste land before exiting
            File.WriteAllText(args.Length > 2 ? args[2] : "testinsert_report.txt", $"strategy: {strategy}");
            return;
        }

        using var mutex = new Mutex(true, @"Local\FlowDictate", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("FlowDictate is already running (check the system tray).",
                "FlowDictate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }

    private static async Task SelfTest(string wavPath, string reportPath, string? selectionPath = null)
    {
        var report = new StreamWriter(reportPath) { AutoFlush = true };
        try
        {
            var settings = AppSettings.Load();
            report.WriteLine($"wav: {wavPath}");

            // Load wav and resample to 16k mono float.
            using var reader = new AudioFileReader(wavPath); // gives float samples
            var resampler = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(reader, 16000);
            var mono = resampler.WaveFormat.Channels == 1
                ? (ISampleProvider)resampler
                : new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(resampler);
            var samples = new List<float>();
            var buffer = new float[16000];
            int read;
            while ((read = mono.Read(buffer, 0, buffer.Length)) > 0)
                samples.AddRange(buffer.Take(read));
            report.WriteLine($"samples: {samples.Count} ({samples.Count / 16000.0:F1}s)");

            using var transcriber = new WhisperTranscriber(settings.WhisperModelPath, settings.Language, settings.CustomDictionary);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await transcriber.InitializeAsync();
            report.WriteLine($"model loaded: {sw.ElapsedMilliseconds} ms");

            sw.Restart();
            string raw = await transcriber.TranscribeAsync(samples.ToArray());
            report.WriteLine($"transcribe: {sw.ElapsedMilliseconds} ms");
            report.WriteLine($"RAW: {raw}");

            ITextCleaner cleaner = settings.AiCleanupEnabled && settings.ResolvedApiKey.Length > 0
                ? new ClaudeCleaner(settings.ResolvedApiKey, settings.ClaudeModel, m => report.WriteLine(m), settings.CustomDictionary)
                : new RuleBasedCleaner();
            report.WriteLine($"cleaner: {cleaner.Name}");
            sw.Restart();
            string cleaned;
            if (selectionPath is not null && cleaner is ISelectionAwareCleaner sel)
            {
                string selection = File.ReadAllText(selectionPath);
                report.WriteLine($"selection ({selection.Length} chars): {selection}");
                cleaned = await sel.TransformSelectionAsync(raw, selection);
            }
            else
                cleaned = await cleaner.CleanAsync(raw);
            report.WriteLine($"clean: {sw.ElapsedMilliseconds} ms");
            report.WriteLine($"CLEANED: {cleaned}");
            report.WriteLine("SELFTEST OK");
        }
        catch (Exception ex)
        {
            report.WriteLine($"SELFTEST FAILED: {ex}");
        }
        finally
        {
            report.Dispose();
        }
    }
}
