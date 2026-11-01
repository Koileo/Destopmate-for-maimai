namespace DesktopMateOnMaimai.Core.Motion
{
    /// <summary>
    /// 一帧的姿态：45 个语义通道，正交于任何具体骨架。
    ///
    /// 为什么用「语义角度」而不是直接写骨骼四元数：
    /// 本 Mod 支持几十个转换来的角色，骨骼 local 轴（roll）各不相同，
    /// 直接按 local X/Y/Z 施加旋转在 T-pose 手臂上只会把手臂拧麻花。
    /// 因此姿态只用「向下多少度 / 向前多少度 / 弯多少度」这类可读量描述，
    /// 由 PoseBinder 统一换算成各骨架的父子空间旋转。
    ///
    /// 角度单位：度。位移单位：米（角色归一化后身高 1.8）。
    /// 约定：pitch 正值 = 向前/低头；yaw 正值 = 转向角色左侧(+X)；roll 正值 = 向右侧倾。
    /// </summary>
    internal sealed class Pose
    {
        public const int HipsPitch = 0;
        public const int HipsYaw = 1;
        public const int HipsRoll = 2;
        public const int HipsShiftX = 3;      // 米，重心左右
        public const int HipsDropY = 4;       // 米，重心上下
        public const int Spine1Pitch = 5;
        public const int Spine1Yaw = 6;
        public const int Spine1Roll = 7;
        public const int Spine2Pitch = 8;
        public const int Spine2Yaw = 9;
        public const int Spine2Roll = 10;
        public const int ChestPitch = 11;
        public const int ChestYaw = 12;
        public const int ChestRoll = 13;
        public const int NeckPitch = 14;
        public const int NeckYaw = 15;
        public const int NeckRoll = 16;
        public const int HeadPitch = 17;
        public const int HeadYaw = 18;
        public const int HeadRoll = 19;
        public const int ShoulderLiftL = 20;  // 耸肩，正值抬
        public const int ShoulderLiftR = 21;
        public const int ArmDownL = 22;       // 相对水平：0 = T-pose，90 = 垂在体侧
        public const int ArmDownR = 23;
        public const int ArmFwdL = 24;        // 上臂前摆
        public const int ArmFwdR = 25;
        public const int ArmTwistL = 26;
        public const int ArmTwistR = 27;
        public const int ElbowL = 28;         // 屈肘，正值
        public const int ElbowR = 29;
        public const int WristL = 30;         // 腕上下摆
        public const int WristR = 31;
        public const int WristSideL = 32;     // 腕内外摆
        public const int WristSideR = 33;
        public const int ThighFwdL = 34;
        public const int ThighFwdR = 35;
        public const int ThighOutL = 36;
        public const int ThighOutR = 37;
        public const int KneeL = 38;          // 屈膝，正值（脚跟向后）
        public const int KneeR = 39;
        public const int AnkleL = 40;         // 正值 = 压脚背/踮脚
        public const int AnkleR = 41;
        public const int Blink = 42;          // 0..1
        public const int Smile = 43;          // 0..1
        public const int MouthOpen = 44;      // 0..1
        public const int Squint = 45;         // 0..1
        public const int EyeYaw = 46;         // 眼球单独偏转（软钳制后剩余的头部角度由 HeadYaw 承担）
        public const int EyePitch = 47;

        public const int Count = 48;

        public readonly float[] V = new float[Count];

        public float this[int index]
        {
            get { return V[index]; }
            set { V[index] = value; }
        }

        public void Reset()
        {
            for (int i = 0; i < Count; i++) V[i] = 0f;
        }

        public void CopyFrom(Pose src)
        {
            for (int i = 0; i < Count; i++) V[i] = src.V[i];
        }

        public static void Lerp(Pose dst, Pose a, Pose b, float t)
        {
            if (t <= 0f) { a.CopyTo(dst); return; }
            if (t >= 1f) { b.CopyTo(dst); return; }
            for (int i = 0; i < Count; i++) dst.V[i] = a.V[i] + (b.V[i] - a.V[i]) * t;
        }

        private void CopyTo(Pose dst)
        {
            for (int i = 0; i < Count; i++) dst.V[i] = V[i];
        }

        /// <summary>dst += (a - b) * w。手势层用「相对静止位」的增量叠加，呼吸/视线才不会被顶掉。</summary>
        public static void AddDelta(Pose dst, Pose a, Pose b, float w)
        {
            if (w == 0f) return;
            for (int i = 0; i < Count; i++) dst.V[i] += (a.V[i] - b.V[i]) * w;
        }

        /// <summary>只缩身体通道（0..AnkleR）的幅度，表情通道不动，对应 GestureIntensity。</summary>
        public void ScaleMotion(float scale)
        {
            for (int i = HipsPitch; i <= AnkleR; i++) V[i] *= scale;
        }
    }
}
