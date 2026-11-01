using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopMateOnMaimai.Core.Motion
{
    /// <summary>
    /// 把语义姿态写回骨架。
    ///
    /// 三个关键决定：
    /// 1) 旋转按「根空间语义轴」逐级累加（X=右, Y=上, Z=前），最后统一算成
    ///    <c>worldRot = 根增量 * 累积旋转 * 绑定旋转</c>。不管某个角色的骨骼 roll 是多少，
    ///    抬手臂就是抬手臂，不会变成拧麻花（旧实现按 local X 转 -65°，在 T-pose 手臂上只是把手拧了一下）。
    /// 2) 绑定姿势可能是 A-pose 而非 T-pose。绑定期量一次「手臂相对水平线向下多少度」，
    ///    运行时减掉这个基准，于是 ArmDown=74 在所有角色上都等于「自然垂手」。
    /// 3) 一段动作结束时关节不停在原位，而是继续由 MotionRig 的呼吸/噪声层接手，避免"啪"地定住。
    /// </summary>
    internal sealed class PoseBinder
    {
        private struct Joint
        {
            public Transform T;
            public Quaternion Rest;   // 绑定时的世界旋转
        }

        private struct Morph
        {
            public SkinnedMeshRenderer R;
            public int Index;
        }

        private sealed class MorphChannel
        {
            private readonly List<Morph> _preferred = new List<Morph>();
            private readonly List<Morph> _fallback = new List<Morph>();
            private float _last = -1f;

            public void Add(Morph morph, bool preferred)
            {
                if (preferred) _preferred.Add(morph);
                else _fallback.Add(morph);
            }

            public void Apply(float weight01)
            {
                // 有「整体」形变就只用整体，避免 Eye_Blink 与 Eye_Blink_L 叠加成两倍闭眼
                List<Morph> list = _preferred.Count > 0 ? _preferred : _fallback;
                if (list.Count == 0) return;
                float value = Mathf.Clamp01(weight01) * 100f;
                // 值没变就不写：每次 SetBlendShapeWeight 都会让蒙皮重算，白烧 CPU
                if (Mathf.Abs(value - _last) < 0.25f) return;
                _last = value;
                for (int i = 0; i < list.Count; i++)
                    list[i].R.SetBlendShapeWeight(list[i].Index, value);
            }
        }

        private readonly Transform _root;
        private readonly Quaternion _restRootRot;

        private Joint _hips, _spine1, _spine2, _chest, _neck, _head;
        private Joint _shoulderL, _shoulderR, _armL, _armR, _forearmL, _forearmR, _handL, _handR;
        private Joint _thighL, _thighR, _shinL, _shinR, _footL, _footR;
        private Joint _eyeL, _eyeR;

        private bool _chestSharesSpine2;
        private bool _neckSharesHead;

        private Vector3 _hipsRestLocal;
        private bool _hipsPlaced;

        private float _armSideL = 1f, _armSideR = -1f;
        private float _armRestDownL, _armRestDownR;
        private float _legSideL = 1f, _legSideR = -1f;

        private readonly MorphChannel _blink = new MorphChannel();
        private readonly MorphChannel _smile = new MorphChannel();
        private readonly MorphChannel _mouth = new MorphChannel();
        private readonly MorphChannel _squint = new MorphChannel();

        public bool Ready { get; private set; }
        public bool HasEyes { get; private set; }

        public PoseBinder(PortableCharacter model)
        {
            if (model == null || model.Root == null || model.Nodes == null) return;
            _root = model.Root.transform;
            _restRootRot = _root.rotation;

            var byName = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in model.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.name)) continue;
                if (!byName.ContainsKey(node.name)) byName[node.name] = node;
            }

            Bind(out _hips, Get(byName, "Hips", "Root"));
            Bind(out _spine1, Get(byName, "Spine1", "Spine"));
            Bind(out _spine2, Get(byName, "Spine2", "Chest"));
            Bind(out _chest, Get(byName, "Chest", "UpperChest"));
            Bind(out _neck, Get(byName, "Neck"));
            Bind(out _head, Get(byName, "Head"));
            Bind(out _shoulderL, Get(byName, "Shoulder_Left", "L_Shoulder"));
            Bind(out _shoulderR, Get(byName, "Shoulder_Right", "R_Shoulder"));
            Bind(out _armL, Get(byName, "Arm_Left", "UpperArm_Left", "L_UpperArm"));
            Bind(out _armR, Get(byName, "Arm_Right", "UpperArm_Right", "R_UpperArm"));
            Bind(out _forearmL, Get(byName, "ForeArm_Left", "LowerArm_Left", "L_LowerArm"));
            Bind(out _forearmR, Get(byName, "ForeArm_Right", "LowerArm_Right", "R_LowerArm"));
            Bind(out _handL, Get(byName, "Hand_Left", "L_Hand"));
            Bind(out _handR, Get(byName, "Hand_Right", "R_Hand"));
            Bind(out _thighL, Get(byName, "UpLeg_Left", "UpperLeg_Left", "L_UpperLeg"));
            Bind(out _thighR, Get(byName, "UpLeg_Right", "UpperLeg_Right", "R_UpperLeg"));
            Bind(out _shinL, Get(byName, "Leg_Left", "LowerLeg_Left", "L_LowerLeg"));
            Bind(out _shinR, Get(byName, "Leg_Right", "LowerLeg_Right", "R_LowerLeg"));
            Bind(out _footL, Get(byName, "Foot_Left", "L_Foot"));
            Bind(out _footR, Get(byName, "Foot_Right", "R_Foot"));
            Bind(out _eyeL, Get(byName, "Eye_Left", "L_Eye"));
            Bind(out _eyeR, Get(byName, "Eye_Right", "R_Eye"));

            _chestSharesSpine2 = _chest.T != null && _spine2.T != null && _chest.T == _spine2.T;
            _neckSharesHead = _neck.T != null && _head.T != null && _neck.T == _head.T;

            if (_hips.T != null)
            {
                _hipsRestLocal = _hips.T.localPosition;
                _hipsPlaced = true;
            }

            _armSideL = SideOf(_shoulderL.T, _armL.T, _forearmL.T, 1f);
            _armSideR = SideOf(_shoulderR.T, _armR.T, _forearmR.T, -1f);
            _armRestDownL = RestDownAngle(_armL.T, _forearmL.T);
            _armRestDownR = RestDownAngle(_armR.T, _forearmR.T);
            _legSideL = SideOf(null, _thighL.T, _shinL.T, 1f);
            _legSideR = SideOf(null, _thighR.T, _shinR.T, -1f);

            ScanMorphs(model);

            HasEyes = _eyeL.T != null || _eyeR.T != null;
            Ready = _head.T != null || _hips.T != null;
        }

        private static Transform Get(Dictionary<string, Transform> map, string a, string b = null, string c = null)
        {
            Transform t;
            if (!string.IsNullOrEmpty(a) && map.TryGetValue(a, out t)) return t;
            if (!string.IsNullOrEmpty(b) && map.TryGetValue(b, out t)) return t;
            if (!string.IsNullOrEmpty(c) && map.TryGetValue(c, out t)) return t;
            return null;
        }

        private void Bind(out Joint joint, Transform t)
        {
            joint = new Joint();
            if (t == null) return;
            joint.T = t;
            joint.Rest = t.rotation;
        }

        /// <summary>量左右：VRM 约定角色左侧为 +X。</summary>
        private float SideOf(Transform mount, Transform limb, Transform child, float fallback)
        {
            Transform reference = mount != null ? mount : limb;
            if (reference == null || child == null) return fallback;
            Vector3 dir = _root.InverseTransformDirection(child.position - reference.position);
            if (Mathf.Abs(dir.x) < 0.0005f) return fallback;
            return dir.x > 0f ? 1f : -1f;
        }

        /// <summary>绑定姿势下手臂相对水平线往下多少度（T-pose=0，A-pose&gt;0）。</summary>
        private float RestDownAngle(Transform arm, Transform forearm)
        {
            if (arm == null || forearm == null) return 0f;
            Vector3 dir = _root.InverseTransformDirection(forearm.position - arm.position).normalized;
            return -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        // ---------------- 表情 ----------------

        private static readonly string[] BlinkPreferred = { "eye_blink", "fcl_eye_close", "blink", "eyeclose" };
        private static readonly string[] SmilePreferred = { "eye_smile1", "fcl_eye_joy", "eye_smile" };
        private static readonly string[] MouthPreferred = { "mouth_a", "fcl_mth_a", "ah" };
        private static readonly string[] SquintPreferred = { "eye_small" };

        private void ScanMorphs(PortableCharacter model)
        {
            if (model.Renderers == null) return;
            foreach (var renderer in model.Renderers)
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                var mesh = renderer.sharedMesh;
                int count = mesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    string raw = mesh.GetBlendShapeName(i);
                    if (string.IsNullOrEmpty(raw)) continue;
                    string name = raw.ToLowerInvariant();
                    var morph = new Morph { R = renderer, Index = i };

                    if (name.Contains("blink"))
                        _blink.Add(morph, Matches(name, BlinkPreferred));
                    else if (name.Contains("smile") || name.Contains("joy"))
                        _smile.Add(morph, Matches(name, SmilePreferred));
                    else if (name.Contains("mouth_a") || name.Contains("mth_a") || name == "ah")
                        _mouth.Add(morph, Matches(name, MouthPreferred));
                    else if (name.Contains("eye_small") || name.Contains("jito"))
                        _squint.Add(morph, Matches(name, SquintPreferred));
                }
            }
        }

        private static bool Matches(string name, string[] preferred)
        {
            for (int i = 0; i < preferred.Length; i++)
                if (string.Equals(name, preferred[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------------- 每帧写入 ----------------

        public void Apply(Pose p)
        {
            if (!Ready) return;

            Quaternion rt = _root.rotation * Quaternion.Inverse(_restRootRot);

            // ---- 躯干：自下而上累加，子骨骼继承父骨骼的累积旋转 ----
            Quaternion qHips = Quaternion.Euler(p[Pose.HipsPitch], p[Pose.HipsYaw], p[Pose.HipsRoll]);
            SetJoint(ref _hips, rt * qHips);
            PlaceHips(rt, p[Pose.HipsShiftX], p[Pose.HipsDropY]);

            float s2Pitch = p[Pose.Spine2Pitch], s2Yaw = p[Pose.Spine2Yaw], s2Roll = p[Pose.Spine2Roll];
            if (_chestSharesSpine2)
            {
                s2Pitch += p[Pose.ChestPitch];
                s2Yaw += p[Pose.ChestYaw];
                s2Roll += p[Pose.ChestRoll];
            }

            Quaternion q = qHips * Quaternion.Euler(p[Pose.Spine1Pitch], p[Pose.Spine1Yaw], p[Pose.Spine1Roll]);
            SetJoint(ref _spine1, rt * q);
            q = q * Quaternion.Euler(s2Pitch, s2Yaw, s2Roll);
            SetJoint(ref _spine2, rt * q);
            if (!_chestSharesSpine2)
            {
                q = q * Quaternion.Euler(p[Pose.ChestPitch], p[Pose.ChestYaw], p[Pose.ChestRoll]);
                SetJoint(ref _chest, rt * q);
            }
            Quaternion qChest = q;

            float headPitch = p[Pose.HeadPitch], headYaw = p[Pose.HeadYaw], headRoll = p[Pose.HeadRoll];
            if (_neckSharesHead)
            {
                headPitch += p[Pose.NeckPitch];
                headYaw += p[Pose.NeckYaw];
                headRoll += p[Pose.NeckRoll];
            }
            else
            {
                q = q * Quaternion.Euler(p[Pose.NeckPitch], p[Pose.NeckYaw], p[Pose.NeckRoll]);
                SetJoint(ref _neck, rt * q);
            }
            q = q * Quaternion.Euler(headPitch, headYaw, headRoll);
            SetJoint(ref _head, rt * q);
            Quaternion qHead = q;

            // ---- 手臂：肩 → 上臂 → 前臂 → 手 ----
            Arm(ref _shoulderL, ref _armL, ref _forearmL, ref _handL, qChest, rt, _armSideL,
                p[Pose.ShoulderLiftL], p[Pose.ArmDownL] - _armRestDownL, p[Pose.ArmFwdL],
                p[Pose.ArmTwistL], p[Pose.ElbowL], p[Pose.WristL], p[Pose.WristSideL]);
            Arm(ref _shoulderR, ref _armR, ref _forearmR, ref _handR, qChest, rt, _armSideR,
                p[Pose.ShoulderLiftR], p[Pose.ArmDownR] - _armRestDownR, p[Pose.ArmFwdR],
                p[Pose.ArmTwistR], p[Pose.ElbowR], p[Pose.WristR], p[Pose.WristSideR]);

            // ---- 腿：胯 → 大腿 → 小腿 → 脚 ----
            Leg(ref _thighL, ref _shinL, ref _footL, qHips, rt, _legSideL,
                p[Pose.ThighFwdL], p[Pose.ThighOutL], p[Pose.KneeL], p[Pose.AnkleL]);
            Leg(ref _thighR, ref _shinR, ref _footR, qHips, rt, _legSideR,
                p[Pose.ThighFwdR], p[Pose.ThighOutR], p[Pose.KneeR], p[Pose.AnkleR]);

            // ---- 眼球：头部只转一部分，剩下交给眼睛 ----
            if (HasEyes)
            {
                Quaternion qEye = Quaternion.AngleAxis(p[Pose.EyeYaw], Vector3.up)
                                  * Quaternion.AngleAxis(-p[Pose.EyePitch], Vector3.right);
                SetJoint(ref _eyeL, rt * qHead * qEye);
                SetJoint(ref _eyeR, rt * qHead * qEye);
            }

            _blink.Apply(p[Pose.Blink]);
            _smile.Apply(p[Pose.Smile]);
            _mouth.Apply(p[Pose.MouthOpen]);
            _squint.Apply(p[Pose.Squint]);
        }

        private void SetJoint(ref Joint joint, Quaternion worldRot)
        {
            if (joint.T == null) return;
            joint.T.rotation = worldRot * joint.Rest;
        }

        /// <summary>胯位移用 localPosition 写入，避开模型根被移动到 (20000,20000) 后的坐标系问题。</summary>
        private void PlaceHips(Quaternion rt, float shift, float drop)
        {
            if (!_hipsPlaced || _hips.T == null) return;
            if (Mathf.Abs(shift) < 0.00001f && Mathf.Abs(drop) < 0.00001f)
            {
                _hips.T.localPosition = _hipsRestLocal;
                return;
            }
            Vector3 worldOffset = rt * new Vector3(shift, drop, 0f);
            Transform parent = _hips.T.parent;
            Vector3 localOffset = parent != null ? parent.InverseTransformVector(worldOffset) : worldOffset;
            _hips.T.localPosition = _hipsRestLocal + localOffset;
        }

        private void Arm(ref Joint shoulder, ref Joint arm, ref Joint fore, ref Joint hand, Quaternion qChest,
            Quaternion rt, float side, float lift, float down, float fwd, float twist,
            float elbow, float wrist, float wristSide)
        {
            // 肩：绕角色前后轴上下，正角抬肩
            Quaternion qShoulder = qChest * Quaternion.AngleAxis(side * lift, Vector3.forward);
            SetJoint(ref shoulder, rt * qShoulder);

            // 上臂：先外展（绕 Z 落下），再前摆（绕 Y），最后绕 X 捻转
            Quaternion armQ = Quaternion.AngleAxis(-side * fwd, Vector3.up)
                              * Quaternion.AngleAxis(-side * down, Vector3.forward)
                              * Quaternion.AngleAxis(side * twist, Vector3.right);
            Quaternion qArm = qShoulder * armQ;
            SetJoint(ref arm, rt * qArm);

            // 肘：绕上臂"向前弯"的轴；轴随上臂一起转
            Quaternion qFore = qArm * Quaternion.AngleAxis(Mathf.Max(0f, elbow), -side * Vector3.up);
            SetJoint(ref fore, rt * qFore);

            // 腕：同平面的上下摆 + 绕前臂轴的侧摆
            Quaternion qHand = qFore
                               * Quaternion.AngleAxis(-side * wrist, -side * Vector3.up)
                               * Quaternion.AngleAxis(side * wristSide, Vector3.right);
            SetJoint(ref hand, rt * qHand);
        }

        private void Leg(ref Joint thigh, ref Joint shin, ref Joint foot, Quaternion qHips,
            Quaternion rt, float side, float fwd, float outward, float knee, float ankle)
        {
            // 大腿：向前抬（绕 -X），向外展（绕 +Z*side；腿骨朝下，符号与手臂相反）
            Quaternion qThigh = qHips
                                * Quaternion.AngleAxis(-fwd, Vector3.right)
                                * Quaternion.AngleAxis(side * outward, Vector3.forward);
            SetJoint(ref thigh, rt * qThigh);

            // 膝：绕大腿的左右轴向后弯
            Quaternion qShin = qThigh * Quaternion.AngleAxis(Mathf.Max(0f, knee), Vector3.right);
            SetJoint(ref shin, rt * qShin);

            // 踝：正值压脚背
            Quaternion qFoot = qShin * Quaternion.AngleAxis(ankle, Vector3.right);
            SetJoint(ref foot, rt * qFoot);
        }
    }
}
