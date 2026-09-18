using System.Globalization;
using System.Speech.Recognition;

namespace ApexClick.FeaturePack.Audio;

public sealed record VoiceTriggerOptions
{
    public bool Enabled { get; init; }
    public string Phrase { get; init; } = "ApexClick";
    public string Culture { get; init; } = "en-US";
    public float ConfidenceThreshold { get; init; } = 0.72f;
}

public sealed class VoiceTriggerService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private bool _disposed;
    private float _confidenceThreshold = 0.72f;
    private readonly object _sync = new();

    public bool IsListening { get; private set; }
    public event EventHandler<string>? PhraseRecognized;
    public event EventHandler<string>? Error;

    public void Start(VoiceTriggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.Phrase))
            return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(options.Culture);
            var engine = new SpeechRecognitionEngine(culture);
            var choices = new Choices(options.Phrase.Trim());
            var grammar = new Grammar(new GrammarBuilder(choices));
            _confidenceThreshold = Math.Clamp(options.ConfidenceThreshold, 0.05f, 0.99f);
            engine.LoadGrammar(grammar);
            engine.SpeechRecognized += OnSpeechRecognized;
            engine.RecognizeCompleted += OnRecognizeCompleted;
            engine.SetInputToDefaultAudioDevice();
            engine.RecognizeAsync(RecognizeMode.Multiple);
            lock (_sync)
            {
                _engine = engine;
                IsListening = true;
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex.Message);
            Stop();
        }
    }

    public void Stop()
    {
        SpeechRecognitionEngine? engine;
        lock (_sync)
        {
            engine = _engine;
            _engine = null;
            IsListening = false;
        }

        if (engine is null) return;
        try
        {
            engine.RecognizeAsyncCancel();
            engine.RecognizeAsyncStop();
        }
        catch { }
        engine.SpeechRecognized -= OnSpeechRecognized;
        engine.RecognizeCompleted -= OnRecognizeCompleted;
        engine.Dispose();
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence < _confidenceThreshold) return;
        PhraseRecognized?.Invoke(this, e.Result.Text);
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (e.Error is not null && !e.Cancelled)
            Error?.Invoke(this, e.Error.Message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
