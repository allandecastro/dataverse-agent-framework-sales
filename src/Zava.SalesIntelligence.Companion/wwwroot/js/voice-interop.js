// Browser voice for the companion: Azure Speech STT + TTS over the Foundry resource, using a
// short-lived token from /api/speech-token so the key never reaches the browser. Speech only
// transcribes and synthesises - the conversation stays the only brain.
window.voiceInterop = (() => {
    let cached = null; // { token, region, at }

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
        config.speechSynthesisVoiceName = 'en-US-AvaNeural';
        return config;
    }

    return {
        // Listen for one utterance from the microphone and return the recognised text (push to
        // talk). The first call prompts for microphone permission.
        recognizeOnce: async function () {
            const config = await getConfig();
            const audio = SpeechSDK.AudioConfig.fromDefaultMicrophoneInput();
            const recognizer = new SpeechSDK.SpeechRecognizer(config, audio);
            try {
                const result = await new Promise((resolve, reject) =>
                    recognizer.recognizeOnceAsync(resolve, reject));
                return result && result.reason === SpeechSDK.ResultReason.RecognizedSpeech ? result.text : '';
            } finally {
                recognizer.close();
            }
        },

        // Read the given text aloud through the default speaker.
        speak: async function (text) {
            if (!text) {
                return;
            }
            const config = await getConfig();
            const synthesizer = new SpeechSDK.SpeechSynthesizer(config);
            try {
                await new Promise((resolve, reject) =>
                    synthesizer.speakTextAsync(text, resolve, reject));
            } finally {
                synthesizer.close();
            }
        }
    };
})();
