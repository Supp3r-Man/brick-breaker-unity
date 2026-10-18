using UnityEngine;

namespace BrickBurst
{
    /// <summary>Sound effects synthesised at start-up (sine/saw/square beeps), so there are no audio files.</summary>
    public sealed class Sfx
    {
        private const int SampleRate = 44100;

        private readonly AudioSource source;
        private readonly AudioClip launch, wall, paddle, brick, burst, power, lose, clear, gameOver;

        public Sfx(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.5f;

            launch = Tone("launch", new[] { 330f, 495f }, 0.06f, Wave.Sine);
            wall = Tone("wall", new[] { 240f }, 0.04f, Wave.Square);
            paddle = Tone("paddle", new[] { 420f }, 0.06f, Wave.Sine);
            brick = Tone("brick", new[] { 660f }, 0.05f, Wave.Square);
            burst = Tone("burst", new[] { 880f, 1175f }, 0.06f, Wave.Square);
            power = Tone("power", new[] { 523f, 784f, 1046f }, 0.07f, Wave.Sine);
            lose = Tone("lose", new[] { 300f, 200f, 120f }, 0.12f, Wave.Saw);
            clear = Tone("clear", new[] { 523f, 659f, 784f, 1046f, 1318f }, 0.09f, Wave.Sine);
            gameOver = Tone("gameover", new[] { 392f, 330f, 262f, 196f }, 0.2f, Wave.Saw);
        }

        public void Launch() { Play(launch); }
        public void Wall() { Play(wall); }
        public void Paddle() { Play(paddle); }
        public void Brick() { Play(brick); }
        public void Burst() { Play(burst); }
        public void Power() { Play(power); }
        public void Lose() { Play(lose); }
        public void Clear() { Play(clear); }
        public void GameOver() { Play(gameOver); }

        private void Play(AudioClip clip)
        {
            if (clip != null) source.PlayOneShot(clip);
        }

        private enum Wave { Sine, Saw, Square }

        /// <summary>One note per frequency, each noteSeconds long, with a quick fade so notes do not click.</summary>
        private static AudioClip Tone(string name, float[] freqs, float noteSeconds, Wave wave)
        {
            int perNote = Mathf.RoundToInt(noteSeconds * SampleRate);
            var data = new float[perNote * freqs.Length];
            for (int n = 0; n < freqs.Length; n++)
            {
                double phase = 0.0;
                double step = freqs[n] / SampleRate;
                for (int i = 0; i < perNote; i++)
                {
                    phase += step;
                    phase -= System.Math.Floor(phase);
                    float sample;
                    if (wave == Wave.Saw) sample = (float)(phase * 2.0 - 1.0);
                    else if (wave == Wave.Square) sample = phase < 0.5 ? 1f : -1f;
                    else sample = Mathf.Sin((float)(phase * 2.0 * System.Math.PI));
                    float t = (float)i / perNote;
                    float env = Mathf.Min(1f, t * 20f) * (1f - t);
                    data[n * perNote + i] = sample * env * 0.35f;
                }
            }
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
