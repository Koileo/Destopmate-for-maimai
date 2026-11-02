using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DesktopMateOnMaimai.Core.Motion
{
    // Version-neutral original humanoid muscle samples. Unity performs the
    // retargeting, including elbows, wrist twist and fingers, on its own avatar.
    internal sealed class NativeMotion : IDisposable
    {
        private readonly Dictionary<string, MuscleClip> _clips = new Dictionary<string, MuscleClip>();
        private readonly float[] _previous = new float[95];
        private readonly float[] _loopStart = new float[95];
        private readonly float[] _standing = new float[95];
        private readonly float[] _smoothed = new float[95];
        private readonly float _idleMotion;
        private readonly bool[] _eyeMuscles = new bool[95];
        private bool _standingSettled;
        private Avatar _avatar;
        private HumanPoseHandler _handler;
        private HumanPose _pose;
        private MuscleClip _current;
        private float _time, _blend = 1f;
        private bool _gesture;
        private bool _seated;
        public bool Busy => _gesture;
        public bool SupportsSeat => _clips.ContainsKey("b1");
        private MuscleClip RestClip => _clips[_seated ? "b1" : "a1"];
        public void SetSeated(bool seated)
        {
            _seated = seated && SupportsSeat;
            _gesture = false;
            Switch(RestClip);
        }

        private static string GestureClip(string trigger)
        {
            switch ((trigger ?? "").ToLowerInvariant())
            {
                case "shift": return "a4";
                case "think": return "a2";
                case "handsonhips": return "a8";
                default: return null;
            }
        }
        public bool SupportsGesture(string trigger)
        {
            string name = GestureClip(trigger);
            return !_seated && name != null && _clips.ContainsKey(name);
        }

        private static int Count(BinaryReader r, int min, int max)
        {
            int value = r.ReadInt32();
            if (value < min || value > max) throw new InvalidDataException("Invalid motion count");
            return value;
        }
        private static string Text(BinaryReader r)
        {
            int n = Count(r, 0, 4096);
            byte[] data = r.ReadBytes(n);
            if (data.Length != n) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(data);
        }
        private static float Number(BinaryReader r)
        {
            float v = r.ReadSingle();
            if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Non-finite motion value");
            return v;
        }
        private static Vector3 Vector(BinaryReader r) => new Vector3(Number(r), Number(r), Number(r));

        public NativeMotion(PortableCharacter model, string path, float idleMotion = 0f)
        {
            _idleMotion = Mathf.Clamp01(idleMotion);
            var muscleNames = HumanTrait.MuscleName;
            for (int i = 0; i < _eyeMuscles.Length; i++)
                _eyeMuscles[i] = muscleNames[i].IndexOf("Eye", StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                HumanBone[] human;
                var reference = new Dictionary<string, SkeletonBone>();
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (Encoding.ASCII.GetString(r.ReadBytes(8)) != "DMMUSC01")
                        throw new InvalidDataException("Unsupported motion format");
                    human = new HumanBone[Count(r, 15, 128)];
                    for (int i = 0; i < human.Length; i++)
                        human[i] = new HumanBone { boneName = Text(r), humanName = Text(r),
                            limit = new HumanLimit { useDefaultValues = true } };
                    int skeletonCount = Count(r, 15, 4096);
                    for (int i = 0; i < skeletonCount; i++)
                    {
                        var bone = new SkeletonBone { name = Text(r), position = Vector(r),
                            rotation = new Quaternion(Number(r), Number(r), Number(r), Number(r)), scale = Vector(r) };
                        reference[bone.name] = bone;
                    }
                    int count = Count(r, 1, 256);
                    for (int i = 0; i < count; i++)
                    {
                        var clip = new MuscleClip { Name = Text(r), Length = Number(r), Rate = Number(r), Frames = Count(r, 2, 36000) };
                        if (clip.Length <= 0f || clip.Length > 1200f || clip.Rate <= 0f || clip.Rate > 240f)
                            throw new InvalidDataException("Invalid motion duration");
                        clip.Values = new float[clip.Frames * 95];
                        for (int j = 0; j < clip.Values.Length; j++)
                        {
                            clip.Values[j] = Number(r);
                            if (Mathf.Abs(clip.Values[j]) > 10f) throw new InvalidDataException("Invalid muscle value");
                        }
                        _clips.Add(clip.Name, clip);
                    }
                }
                if (!_clips.ContainsKey("a1")) throw new InvalidDataException("Missing original standing idle a1");
                // Include the real extra root, and use the source avatar's T pose
                // for mapped bones. Building from the already animated pose is invalid.
                var skeleton = new List<SkeletonBone>();
                foreach (var t in model.Root.GetComponentsInChildren<Transform>(true))
                {
                    SkeletonBone bone;
                    if (!reference.TryGetValue(t.name, out bone))
                        bone = new SkeletonBone { name = t.name, position = t.localPosition,
                            rotation = t.localRotation, scale = t.localScale };
                    skeleton.Add(bone);
                }
                _avatar = AvatarBuilder.BuildHumanAvatar(model.Root, new HumanDescription {
                    human = human, skeleton = skeleton.ToArray(), upperArmTwist = 0.5f,
                    lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0f, legStretch = 0f, feetSpacing = 0f });
                if (_avatar == null || !_avatar.isValid || !_avatar.isHuman)
                    throw new InvalidDataException("Original humanoid avatar could not be rebuilt");
                _handler = new HumanPoseHandler(_avatar, model.Root.transform);
                _handler.GetHumanPose(ref _pose);
                _pose.muscles = new float[95];
                // Keep the body at the source standing anchor. Root travel and yaw
                // are intentionally excluded: this character stays facing the player.
                _current = _clips["a1"];
                _current.Sample(0f, _pose.muscles);
                for (int i = 0; i < 95; i++) if (_eyeMuscles[i]) _pose.muscles[i] = 0f;
                Array.Copy(_pose.muscles, _standing, 95);
                Array.Copy(_pose.muscles, _smoothed, 95);
                _handler.SetHumanPose(ref _pose);
            }
            catch { Dispose(); throw; }
        }

        public void Reset()
        {
            _seated = false;
            _gesture = false;
            Switch(_clips["a1"]);
        }

        private void Switch(MuscleClip clip)
        {
            _standingSettled = false;
            Array.Copy(_pose.muscles, _previous, 95);
            _current = clip;
            _time = _blend = 0f;
        }

        public void Play(string trigger)
        {
            if (!SupportsGesture(trigger)) return;
            string name = GestureClip(trigger);
            MuscleClip clip;
            if (_clips.TryGetValue(name, out clip)) { Switch(clip); _gesture = true; }
        }

        public bool Tick(float dt, float speed)
        {
            if (_standingSettled) return false;
            _time += dt * speed;
            _blend = Mathf.Min(1f, _blend + dt / 0.25f);
            if (_time >= _current.Length)
            {
                if (_gesture) { _gesture = false; Switch(RestClip); }
                else _time = _current.Wrap(_time);
            }
            if (_gesture) _current.Sample(_time, _pose.muscles);
            else _current.SampleLoop(_time, _pose.muscles, _loopStart);
            if (!_seated && !_gesture)
            {
                for (int i = 0; i < 95; i++)
                    _pose.muscles[i] = Mathf.Lerp(_standing[i], _pose.muscles[i], _idleMotion);
            }
            float blend = Mathf.SmoothStep(0f, 1f, _blend);
            float smoothing = 1f - Mathf.Exp(-dt / 0.08f);
            bool fixedStanding = !_seated && !_gesture && _idleMotion == 0f && _blend >= 1f;
            bool settled = fixedStanding;
            for (int i = 0; i < 95; i++)
            {
                float target = Mathf.Lerp(_previous[i], _pose.muscles[i], blend);
                if (_eyeMuscles[i]) target = 0f;
                bool atRest = fixedStanding && Mathf.Abs(target - _smoothed[i]) <= 0.000001f;
                _smoothed[i] = atRest ? target : Mathf.Lerp(_smoothed[i], target, smoothing);
                settled &= atRest;
                _pose.muscles[i] = _smoothed[i];
            }
            _handler.SetHumanPose(ref _pose);
            _standingSettled = settled;
            return true;
        }

        public void Dispose()
        {
            _handler?.Dispose();
            _handler = null;
            if (_avatar != null) UnityEngine.Object.Destroy(_avatar);
            _avatar = null;
        }
    }
}
