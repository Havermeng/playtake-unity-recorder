#if UNITY_EDITOR
using System;
using UnityEngine;

// Observe the listener DSP output without changing the sound heard by the player.
namespace PlayTake
{
public sealed class PlayTakeAudioTap : MonoBehaviour
{
    public Action<float[], int, double> Samples;
    void OnAudioFilterRead(float[] data, int channels) { Samples?.Invoke(data, channels, AudioSettings.dspTime); }
}
}
#endif
