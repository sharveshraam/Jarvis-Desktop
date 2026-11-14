using System.Speech.Recognition;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class WindowsSpeechInputService : ISpeechInputService
{
    public Task<string?> ListenOnceAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var engine = new SpeechRecognitionEngine();
        engine.LoadGrammar(new DictationGrammar());
        engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8);
        engine.EndSilenceTimeout = TimeSpan.FromSeconds(1);
        engine.BabbleTimeout = TimeSpan.FromSeconds(15);
        engine.SetInputToDefaultAudioDevice();

        var completed = new TaskCompletionSource<RecognizeCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<RecognizeCompletedEventArgs> handler = (_, args) => completed.TrySetResult(args);
        engine.RecognizeCompleted += handler;
        using var registration = cancellationToken.Register(() =>
        {
            try { engine.RecognizeAsyncCancel(); }
            catch (InvalidOperationException) { }
        });

        try
        {
            engine.RecognizeAsync(RecognizeMode.Single);
            var result = completed.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).GetAwaiter().GetResult();
            if (result.Error is not null) throw new InvalidOperationException($"Speech recognition failed: {result.Error.Message}", result.Error);
            return result.Result?.Text;
        }
        finally
        {
            engine.RecognizeCompleted -= handler;
            try { engine.RecognizeAsyncCancel(); }
            catch (InvalidOperationException) { }
        }
    }, cancellationToken);
}
