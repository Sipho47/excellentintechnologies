
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class AudioVisualizer : MonoBehaviour
{
    public AudioSource audioSource;
    public Transform barTemplate;

    public int barCount = 32;
    public float spacing = 0.35f;
    public float heightMultiplier = 60f;
    public float smoothSpeed = 12f;
    public float minimumHeight = 0.1f;

    private Transform[] bars;
    // A shorter analysis window follows transients with less delay.
    private readonly float[] spectrum = new float[2048];
    private int[] bandEdges;
    private float[] bandLevels;
    private float[] bandGains;
    private int spectrumSampleRate;
    private const float MaximumBarHeight = 3f;
    private const float ReferenceLevel = 0.003f;
    private bool startAfterFirstFrame;
    private AudioSource startupSource;

    private System.Collections.IEnumerator Start()
    {
        if (!startAfterFirstFrame || startupSource == null)
            yield break;

        AudioClip clip = startupSource.clip;
        if (clip != null)
        {
            if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData())
            {
                Debug.LogError("AudioVisualizer: the audio clip could not be loaded.", this);
                yield break;
            }
            while (clip.loadState == AudioDataLoadState.Loading)
                yield return null;
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                Debug.LogError("AudioVisualizer: audio loading failed.", this);
                yield break;
            }
        }

        // Unlike yield return null, this waits for cameras and the Game view to
        // finish rendering. Audio must not run ahead during scene/shader loading.
        yield return new WaitForEndOfFrame();
        if (startupSource != null && startupSource == audioSource
            && startupSource.isActiveAndEnabled && !startupSource.isPlaying)
        {
            ReadSpectrum();
            startupSource.Play();
        }
    }

    private void OnDestroy()
    {
        if (startAfterFirstFrame && startupSource != null)
            startupSource.playOnAwake = true;
    }

    private static float NonNegative(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
    }

    private void ConfigureBands()
    {
        spectrumSampleRate = AudioSettings.outputSampleRate;
        float nyquist = Mathf.Max(1f, spectrumSampleRate * 0.5f);
        float binWidth = nyquist / spectrum.Length;
        float highestFrequency = Mathf.Min(16000f, nyquist);
        float lowestFrequency = Mathf.Min(40f, highestFrequency * 0.01f);
        int lastBin = Mathf.Clamp(Mathf.FloorToInt(highestFrequency / binWidth),
            bars.Length * 2 + 1, spectrum.Length);
        bandEdges[0] = Mathf.Clamp(Mathf.FloorToInt(lowestFrequency / binWidth),
            1, lastBin - bars.Length * 2);

        for (int i = 1; i <= bars.Length; i++)
        {
            float frequency = lowestFrequency * Mathf.Pow(
                highestFrequency / lowestFrequency, i / (float)bars.Length);
            // Exclusive upper edges; reserve at least two bins for every band.
            bandEdges[i] = Mathf.Clamp(Mathf.FloorToInt(frequency / binWidth),
                bandEdges[i - 1] + 2, lastBin - (bars.Length - i) * 2);
            float centerFrequency = Mathf.Sqrt(bandEdges[i - 1] * (float)bandEdges[i]) * binWidth;
            bandGains[i - 1] = Mathf.Clamp(Mathf.Sqrt(centerFrequency / 250f), 0.4f, 8f);
        }
    }

    void Awake()
    {
        if (barTemplate == null)
        {
            enabled = false;
            return;
        }

        // Preserve the Inspector's automatic-start intent, but hold it until a
        // rendered frame exists. Never take over sources configured for manual play.
        startupSource = audioSource;
        startAfterFirstFrame = startupSource != null && startupSource.isActiveAndEnabled
            && startupSource.playOnAwake;
        if (startAfterFirstFrame)
        {
            startupSource.playOnAwake = false;
            startupSource.Stop();
        }

        barCount = Mathf.Clamp(barCount, 1, 256);
        bars = new Transform[barCount];
        bandEdges = new int[barCount + 1];
        bandLevels = new float[barCount];
        bandGains = new float[barCount];
        ConfigureBands();
        // Request analysis before the first rendered frame so Unity can begin
        // filling its spectrum history as soon as playback starts.
        ReadSpectrum();

        float totalWidth = (barCount - 1) * spacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < barCount; i++)
        {
            Transform bar;

            if (i == 0)
            {
                bar = barTemplate;
            }
            else
            {
                bar = Instantiate(barTemplate, transform);
            }

            bar.name = "Bar" + (i + 1).ToString("00");

            Vector3 position = bar.localPosition;
            position.x = startX + (i * spacing);
            Vector3 scale = bar.localScale;
            scale.y = Mathf.Min(NonNegative(minimumHeight), MaximumBarHeight);
            bar.localScale = scale;
            position.y = scale.y * 0.5f;
            position.z = 0f;
            bar.localPosition = position;

            bars[i] = bar;
        }
    }

    private void ReadSpectrum()
    {
        if (audioSource != null && audioSource.isActiveAndEnabled)
            audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
    }

    void LateUpdate()
    {
        if (bars == null)
            return;

        // Sample after other scripts' Update playback commands, including while
        // stopped, rather than deferring analysis initialization until isPlaying.
        ReadSpectrum();

        float floorHeight = Mathf.Min(NonNegative(minimumHeight), MaximumBarHeight);
        bool playing = audioSource != null && audioSource.isActiveAndEnabled
            && audioSource.isPlaying && !audioSource.mute && audioSource.volume > 0f
            && (!AudioListener.pause || audioSource.ignoreListenerPause)
            && AudioListener.volume > 0f;
        if (!playing)
        {
            ResetHeights(floorHeight);
            return;
        }

        if (spectrumSampleRate != AudioSettings.outputSampleRate)
            ConfigureBands();


        for (int i = 0; i < bars.Length; i++)
        {
            double sumSquares = 0.0;
            for (int bin = bandEdges[i]; bin < bandEdges[i + 1]; bin++)
            {
                float sample = NonNegative(spectrum[bin]);
                sumSquares += (double)sample * sample;
            }

            float rms = (float)System.Math.Sqrt(sumSquares / (bandEdges[i + 1] - bandEdges[i]));
            bandLevels[i] = NonNegative(rms * bandGains[i]);
        }

        // Audio runs independently of game time. Fixed gain prevents bars from
        // swelling as an adaptive normalizer recovers between beats.
        float deltaTime = NonNegative(Time.unscaledDeltaTime);
        float responseSpeed = Mathf.Clamp(NonNegative(smoothSpeed), 1f, 120f);
        float heightRange = MaximumBarHeight - floorHeight;
        // The existing Inspector value of 500 is unity gain, not 50 units of height.
        float outputGain = Mathf.Clamp(NonNegative(heightMultiplier) / 500f, 0f, 4f);

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] == null)
                continue;

            // Mild compression keeps quiet bands visible. A soft ceiling retains
            // height differences during transients instead of hard-clipping the row.
            double level = bandLevels[i] / ((double)ReferenceLevel * 3.0);
            double drivenLevel = System.Math.Pow(level, 0.8) * outputGain;
            // Let the spectrum itself reach zero during silence. A separate
            // waveform gate can flatten valid quiet beats between analysis windows.
            float amplitude = (float)(drivenLevel / (1.0 + drivenLevel));
            float targetHeight = Mathf.Clamp(floorHeight + amplitude * heightRange,
                floorHeight, MaximumBarHeight);

            Vector3 scale = bars[i].localScale;
            float currentHeight = Mathf.Clamp(NonNegative(scale.y), floorHeight, MaximumBarHeight);
            // Apply rising energy on this frame: attack smoothing used to delay
            // the first visible beat. Keep interpolation on the falling edge.
            float interpolation = targetHeight >= currentHeight
                ? 1f
                : 1f - Mathf.Exp(-responseSpeed * 2f * deltaTime);

            scale.y = Mathf.Lerp(
                currentHeight,
                targetHeight,
                interpolation
            );
            if (targetHeight == floorHeight && scale.y - floorHeight < 0.001f)
                scale.y = floorHeight;

            bars[i].localScale = scale;

            Vector3 position = bars[i].localPosition;
            position.y = scale.y / 2f;
            bars[i].localPosition = position;
        }
    }

    private void ResetHeights(float height)
    {
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] == null)
                continue;
            Vector3 scale = bars[i].localScale;
            scale.y = height;
            bars[i].localScale = scale;
            Vector3 position = bars[i].localPosition;
            position.y = height * 0.5f;
            bars[i].localPosition = position;
        }
    }
}
