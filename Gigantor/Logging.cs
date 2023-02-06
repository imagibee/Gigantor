#if UNITY_EDITOR
// Logging from Unity
using UnityEngine;
internal class Logger {
    public static void Log(string text)
    {
        Debug.Log($"{text}");
    }
}

#else
using System;
internal class Logger {
    public static void Log(string text) {
        Console.WriteLine($"{text}");
    }
}
#endif

