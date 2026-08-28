// Browser voice for the companion: Azure Speech STT + TTS over the Foundry resource, using a
// short-lived token from /api/speech-token so the key never reaches the browser. Speech only
// transcribes and synthesises - the conversation stays the only brain.
window.voiceInterop = (() => {
    let cached = null; // { token, region, at }

    // Safari (and Chrome's autoplay policy) only allow audio to start inside a user gesture.
    // Our TTS plays after a server round trip over SignalR, so that gesture is already gone by
    // the time speak() runs. The fix is Web Audio: create and resume one AudioContext during a
    // real click - a capture-phase listener that fires before Blazor's own handling - then play
    // synthesised bytes through it. A resumed context needs no further gesture per sound.
    let audioCtx = null;
    let currentSource = null; // the AudioBufferSourceNode now playing, so we can stop it
    let currentRecognizer = null; // the SpeechRecognizer now listening, so we can cancel it
    function stopCurrent() {
        if (currentSource) {
            try { currentSource.stop(); } catch (e) { /* already stopped */ }
            currentSource = null;
        }
    }
    function unlockAudio() {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) {
            return;
        }
        if (!audioCtx) {
            audioCtx = new Ctx();
        }
        if (audioCtx.state === 'suspended') {
            audioCtx.resume().catch(() => { });
        }
    }
    // Cover every browser's idea of a "user gesture": pointer, touch, keyboard.
    ['pointerdown', 'touchend', 'keydown'].forEach((evt) =>
        document.addEventListener(evt, unlockAudio, { capture: true }));

    // decodeAudioData is promise-based in modern browsers but callback-only in older Safari.
    // Support both so playback decodes anywhere.
    function decode(ctx, data) {
        return new Promise((resolve, reject) => {
            const maybePromise = ctx.decodeAudioData(data, resolve, reject);
            if (maybePromise && typeof maybePromise.then === 'function') {
                maybePromise.then(resolve, reject);
            }
        });
    }

    async function getConfig() {
        const fresh = cached && (Date.now() - cached.at) < 9 * 60 * 1000;
        if (!fresh) {
            const res = await fetch('/api/speech-token');
            if (!res.ok) {
                throw new Error('speech-token request failed: ' + res.status);
            }
            const { token, region } = await res.json();
            cached = { token, region, at: Date.now() };
        }
        const config = SpeechSDK.SpeechConfig.fromAuthorizationToken(cached.token, cached.region);
        config.speechRecognitionLanguage = 'en-US';
        // Leave the synthesis voice at the service default (always available); pin one later if wanted.
        return config;
    }

    return {
        // Listen for one utterance from the microphone and return the recognised text (push to
        // talk). The first call prompts for microphone permission.
        recognizeOnce: async function () {
            const config = await getConfig();
            const audio = SpeechSDK.AudioConfig.fromDefaultMicrophoneInput();
            const recognizer = new SpeechSDK.SpeechRecognizer(config, audio);
            currentRecognizer = recognizer;
            try {
                const result = await new Promise((resolve, reject) =>
                    recognizer.recognizeOnceAsync(resolve, reject));
                if (result.reason === SpeechSDK.ResultReason.RecognizedSpeech) {
                    return result.text;
                }
                // NoMatch = heard nothing; that is expected, not an error.
                if (result.reason === SpeechSDK.ResultReason.Canceled) {
                    const details = SpeechSDK.CancellationDetails.fromResult(result);
                    // Surface a real failure (bad token, denied mic, network) instead of a silent
                    // empty string that looks like a dead mic on stage. A user-initiated stop
                    // cancels without an error reason - treat that as "heard nothing".
                    if (details.reason === SpeechSDK.CancellationReason.Error) {
                        throw new Error('speech recognition failed: ' + (details.errorDetails || details.reason));
                    }
                }
                return '';
            } finally {
                currentRecognizer = null;
                try { recognizer.close(); } catch (e) { /* already closed by stopListening */ }
            }
        },

        // Cancel an in-progress listen (the stop button while the mic is capturing), so the UI
        // never locks until Azure's silence timeout. recognizeOnce then returns '' (not an error).
        stopListening: function () {
            if (currentRecognizer) {
                try { currentRecognizer.close(); } catch (e) { /* ignore */ }
                currentRecognizer = null;
            }
        },

        // Read the given text aloud. We synthesise to bytes (pass null for the audio config so
        // the SDK never opens its own suspended speaker) and play them through the AudioContext
        // that the click unlocked. Any failure - a canceled synthesis or a context that never
        // got a gesture - throws, so it shows as "(voice error: ...)" instead of silent nothing.
        speak: async function (text) {
            if (!text) {
                return;
            }
            const config = await getConfig();
            config.speechSynthesisOutputFormat = SpeechSDK.SpeechSynthesisOutputFormat.Audio24Khz96KBitRateMonoMp3;
            const synthesizer = new SpeechSDK.SpeechSynthesizer(config, null);
            let audioData;
            try {
                const result = await new Promise((resolve, reject) =>
                    synthesizer.speakTextAsync(text, resolve, reject));
                // speakTextAsync resolves even when the synthesis was canceled - surface it.
                if (result.reason === SpeechSDK.ResultReason.Canceled) {
                    const details = SpeechSDK.SpeechSynthesisCancellationDetails.fromResult(result);
                    throw new Error('speech synthesis canceled: ' + (details.errorDetails || details.reason));
                }
                audioData = result.audioData;
            } finally {
                synthesizer.close();
            }

            unlockAudio();
            if (!audioCtx) {
                throw new Error('no AudioContext - the browser blocked audio playback');
            }
            if (audioCtx.state === 'suspended') {
                await audioCtx.resume();
            }
            // decodeAudioData detaches the buffer it is given, so hand it a copy.
            const buffer = await decode(audioCtx, audioData.slice(0));
            await new Promise((resolve) => {
                const source = audioCtx.createBufferSource();
                source.buffer = buffer;
                source.connect(audioCtx.destination);
                // stop() also fires onended, so stopSpeaking() resolves this the same way.
                source.onended = () => {
                    if (currentSource === source) {
                        currentSource = null;
                    }
                    resolve();
                };
                stopCurrent(); // interrupt anything still playing before starting the new line
                currentSource = source;
                source.start();
            });
        },

        // Cut playback short - the stop button while the answer is being read aloud.
        stopSpeaking: function () {
            stopCurrent();
        }
    };
})();
