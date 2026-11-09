#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

// Temporary Play-only object. Nothing is serialized into the user's scenes.
namespace PlayTake
{
public sealed class PlayTakeLoop : MonoBehaviour
{
    public Action Frame;
    IEnumerator Start()
    {
        while (true) { yield return new WaitForEndOfFrame(); Frame?.Invoke(); }
    }
}
}
#endif
