using System.Speech.Synthesis;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class WindowsSpeechOutputService : ISpeechOutputService
{
    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        cancellationToken.ThrowIfCancellationRequested();
        using var synthesizer = new SpeechSynthesizer();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SpeakCompletedEventArgs> handler = (_, args) =>
        {
            if (args.Error is not null) completed.TrySetException(args.Error);
            else if (args.Cancelled) completed.TrySetCanceled(cancellationToken);
            else completed.TrySetResult();
        };
        synthesizer.SpeakCompleted += handler;
        using var registration = cancellationToken.Register(() =>
        {
            try { synthesizer.SpeakAsyncCancelAll(); }
            catch (InvalidOperationException) { }
        });
        try
        {
            synthesizer.SpeakAsync(text);
            await completed.Task.WaitAsync(cancellationToken);
        }
        finally { synthesizer.SpeakCompleted -= handler; }
    }
}
