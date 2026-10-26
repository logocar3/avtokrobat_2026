// The car's on-board controller for the Unity engine: a C# mirror of the C firmware
// (src/firmware/ak_controller.c), run every physics step (1 kHz). It sees only what the
// car can measure - here the Hall motor sensor (6 steps per electrical revolution, so the
// speed is a step count over a sliding window) - and outputs a steering command and a
// motor TORQUE request for the VESC.
//
//   motor mode "torque": request = motor_ref (N m), clamped to the VESC's torque range
//   motor mode "speed" : PI on the Hall speed estimate -> torque, with conditional
//                        integration anti-windup (same gains / window as the firmware);
//                        torque that opposes the motion (braking) is capped at
//                        brake_torque_fraction x max, like an RC ESC's brake strength
using UnityEngine;

namespace Avtokrobat
{
    public enum MotorMode { Torque, Speed }

    public class UdircController
    {
        const int Hist = 256;                   // >= speed_window / dt samples
        readonly float maxTorque, maxSteer, kp, ki, window, stepRad, maxBrake;
        readonly double[] histT = new double[Hist];
        readonly long[] histCount = new long[Hist];
        int head, n;
        float integ;

        public float SpeedEstimate { get; private set; }   // motor rad/s, from Hall steps
        public long HallCount { get; private set; }
        public float TorqueCmd { get; private set; }        // N m request to the VESC
        public float SteerCmd { get; private set; }         // -1..1, + = left

        public UdircController(float maxTorque, float maxSteerRad, float kp, float ki,
                               float speedWindow, int polePairs, float brakeFraction = 1f)
        {
            maxBrake = maxTorque * Mathf.Clamp01(brakeFraction);
            this.maxTorque = maxTorque;
            maxSteer = maxSteerRad;
            this.kp = kp;
            this.ki = ki;
            window = speedWindow;
            stepRad = (Mathf.PI / 3f) / Mathf.Max(1, polePairs);
            Reset();
        }

        public void Reset()
        {
            head = n = 0;
            integ = 0f;
            SpeedEstimate = TorqueCmd = SteerCmd = 0f;
        }

        // motorAngle: true motor shaft angle (rad); the Hall sensor quantises it.
        public void Step(double t, float dt, float motorAngle, MotorMode mode, float motorRef, float steerRefRad)
        {
            HallCount = (long)System.Math.Floor(motorAngle / stepRad);
            UpdateSpeed(t);
            float torque;
            if (mode == MotorMode.Speed) torque = SpeedPi(motorRef, dt);
            else { integ = 0f; torque = Mathf.Clamp(motorRef, -maxTorque, maxTorque); }
            TorqueCmd = torque;
            SteerCmd = maxSteer > 0f ? Mathf.Clamp(steerRefRad / maxSteer, -1f, 1f) : 0f;
        }

        void UpdateSpeed(double t)
        {
            histT[head] = t;
            histCount[head] = HallCount;
            head = (head + 1) % Hist;
            if (n < Hist) n++;
            int oldest = -1;
            for (int i = 1; i < n; i++)
            {
                int k = (head - 1 - i + 2 * Hist) % Hist;
                if (t - histT[k] > window) break;
                oldest = k;
            }
            SpeedEstimate = (oldest >= 0 && t > histT[oldest])
                ? (float)((HallCount - histCount[oldest]) * stepRad / (t - histT[oldest])) : 0f;
        }

        float SpeedPi(float reference, float dt)
        {
            float e = reference - SpeedEstimate;
            float u = kp * e + integ;
            // braking = torque against the direction of rotation: limited to maxBrake
            float lo = SpeedEstimate > 1f ? -maxBrake : -maxTorque, hi = SpeedEstimate < -1f ? maxBrake : maxTorque;
            if (!((u >= hi && e > 0f) || (u <= lo && e < 0f)))
                integ = Mathf.Clamp(integ + ki * e * dt, lo, hi);
            return Mathf.Clamp(kp * e + integ, lo, hi);
        }
    }
}
