using System;
using System.Collections.Generic;

// Only Unity's scalar/vector math and the driver/logging boundary are replaced.
// Scheduling, configuration, clips and motion sampling run their production code.
namespace UnityEngine
{
    internal static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Rad2Deg = 180f / PI;
        public static float Clamp(float x, float lo, float hi) => Math.Clamp(x, lo, hi);
        public static float Clamp01(float x) => Clamp(x, 0f, 1f);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Abs(float x) => Math.Abs(x);
        public static float Sin(float x) => MathF.Sin(x);
        public static float Cos(float x) => MathF.Cos(x);
        public static float Exp(float x) => MathF.Exp(x);
        public static float Floor(float x) => MathF.Floor(x);
        public static int FloorToInt(float x) => (int)MathF.Floor(x);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    }

    internal struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3();
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }
    }
}

namespace MelonLoader
{
    internal static class MelonLogger
    {
        public static void Msg(string message) { }
        public static void Warning(string message) { }
        public static void Error(string message) { }
    }
}

namespace DesktopMateOnMaimai.Core
{
    internal interface ICharacterDriver
    {
        bool Ready { get; }
        bool ManagesBlink { get; }
        bool Busy { get; }
        void SetTrigger(string name);
        void SetBool(string name, bool value);
        void SetFloat(string name, float value);
        void PlayFace(string name);
        void LookAt(UnityEngine.Vector3 point);
        void SetSpeed(float speed);
        void Dispose();
    }

    internal sealed class RecordingDriver : ICharacterDriver
    {
        public readonly List<string> Triggers = new List<string>();
        public bool Ready => true;
        public bool ManagesBlink => true;
        public bool Busy => false;
        public int GazeUpdates;
        public void SetTrigger(string name) => Triggers.Add(name);
        public void SetBool(string name, bool value) { }
        public void SetFloat(string name, float value) { }
        public void PlayFace(string name) { }
        public void LookAt(UnityEngine.Vector3 point) => GazeUpdates++;
        public void SetSpeed(float speed) { }
        public void Dispose() { }
    }
}
