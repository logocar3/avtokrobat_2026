// A VESC driving a sensorless/sensored brushless motor in CURRENT (= torque) control, as
// seen from the car's controller: it receives a torque request, and the motor shaft gets
// a torque some time later, limited by what the battery and motor can actually deliver.
//
//   request ──[command latency]──► I_ref = T / Kt ──[current limits]──► FOC current loop
//                                                   (first order, tau)        │
//   shaft torque = Kt I - friction  ◄────────────────────────────────────────┘
//
// Limits, like VESC's l_current_max / l_current_min / l_in_current_max / l_in_current_min /
// l_max_duty: the phase current is clamped to the motor limits, to what the available
// voltage can push against back-EMF ((±duty_max V_bus - e) / R), and so that the battery
// current stays inside its limits. The battery sags under load (V_oc - R_batt I_batt).
// DC-equivalent motor model: V = e + R I, e = w / KV, T = Kt I with Kt = 60 / (2 pi KV).
//
// The delay has two parts, both settable: the transport latency of the command (UART/CAN
// packet + the VESC's comm thread) and the current loop itself. VESC Tool's default FOC
// current-controller time constant is 1000 us, i.e. ~160 Hz bandwidth.
using System.Collections.Generic;
using UnityEngine;

namespace Avtokrobat
{
    [System.Serializable]
    public class MotorSpec
    {
        public string name = "2845 4700KV";
        public float kv_rpm_per_v = 4700f;
        public float resistance_ohm = 0.012f;          // line-to-line
        public float no_load_current_a = 1.5f;         // friction + iron losses, as a drag torque
        public float rotor_inertia_kgm2 = 2.0e-6f;
        public float max_current_a = 60f;
        public int pole_pairs = 2;
    }

    [System.Serializable]
    public class BatterySpec
    {
        public int cells = 3;
        public float cell_voltage = 4.0f;              // open-circuit, per cell (4.2 = full)
        public float internal_resistance_ohm = 0.03f;  // whole pack
    }

    [System.Serializable]
    public class VescSpec
    {
        public float command_delay_s = 0.002f;         // request -> VESC sees it
        public float current_time_constant_s = 0.001f; // FOC current loop (VESC Tool default 1000 us)
        public float motor_current_max_a = 60f;        // l_current_max
        public float motor_current_min_a = -60f;       // l_current_min (braking / reverse)
        public float battery_current_max_a = 60f;      // l_in_current_max
        public float battery_current_min_a = -20f;     // l_in_current_min (regen)
        public float max_duty = 0.95f;                 // l_max_duty
        public int substeps = 10;                      // internal steps per Step() call
    }

    public class VescModel
    {
        readonly MotorSpec motor;
        readonly BatterySpec batt;
        readonly VescSpec spec;
        readonly float kt, kvRad;                       // N m/A, rad/s per V

        struct Pending { public float t, torque; }
        readonly Queue<Pending> queue = new Queue<Pending>();
        float time, activeRequest;

        // state + telemetry
        public float Current { get; private set; }          // phase current, A (DC equivalent)
        public float CurrentRef { get; private set; }       // after delay + limits, A
        public float Torque { get; private set; }           // on the motor shaft, N m
        public float BusVoltage { get; private set; }
        public float BatteryCurrent { get; private set; }
        public float Duty { get; private set; }
        public float RequestSeen { get; private set; }      // torque request the VESC is acting on
        public float Kt { get { return kt; } }
        public float MaxTorque { get { return kt * Mathf.Min(spec.motor_current_max_a, motor.max_current_a); } }

        public VescModel(MotorSpec motor, BatterySpec batt, VescSpec spec)
        {
            this.motor = motor;
            this.batt = batt;
            this.spec = spec;
            kvRad = motor.kv_rpm_per_v * 2f * Mathf.PI / 60f;
            kt = 1f / kvRad;
            Reset();
        }

        public void Reset()
        {
            queue.Clear();
            time = 0f;
            activeRequest = 0f;
            Current = CurrentRef = Torque = BatteryCurrent = Duty = RequestSeen = 0f;
            BusVoltage = batt.cells * batt.cell_voltage;
        }

        // torqueRequest: N m wanted on the motor shaft (sent at the current time).
        // omega: motor shaft speed, rad/s. Returns the shaft torque for this step.
        public float Step(float dt, float torqueRequest, float omega)
        {
            queue.Enqueue(new Pending { t = time, torque = torqueRequest });
            // the newest request that has arrived by the start of this step becomes active,
            // so a request sent at t acts from t + command_delay_s (0 = this very step)
            while (queue.Count > 0 && queue.Peek().t + spec.command_delay_s <= time + 1e-6f)
                activeRequest = queue.Dequeue().torque;
            RequestSeen = activeRequest;
            time += dt;

            int n = Mathf.Max(1, spec.substeps);
            float h = dt / n;
            float decay = 1f - Mathf.Exp(-h / Mathf.Max(1e-6f, spec.current_time_constant_s));
            float sum = 0f;
            float vOc = batt.cells * batt.cell_voltage;
            float emf = omega / kvRad;
            float R = Mathf.Max(1e-4f, motor.resistance_ohm);
            for (int i = 0; i < n; i++)
            {
                BusVoltage = Mathf.Max(0.1f, vOc - batt.internal_resistance_ohm * BatteryCurrent);
                float vMax = spec.max_duty * BusVoltage;
                float iRef = activeRequest / kt;
                iRef = Mathf.Clamp(iRef, Mathf.Max(spec.motor_current_min_a, -motor.max_current_a),
                                         Mathf.Min(spec.motor_current_max_a, motor.max_current_a));
                // what the bridge can push against the back-EMF
                iRef = Mathf.Clamp(iRef, (-vMax - emf) / R, (vMax - emf) / R);
                // battery current limits: P_batt = V_motor I = V_bus I_batt
                float vMot = emf + R * iRef;
                float iBatt = vMot * iRef / BusVoltage;
                if (iBatt > spec.battery_current_max_a) iRef *= spec.battery_current_max_a / iBatt;
                else if (iBatt < spec.battery_current_min_a) iRef *= spec.battery_current_min_a / iBatt;
                CurrentRef = iRef;

                Current += (iRef - Current) * decay;
                float vNow = emf + R * Current;
                Duty = Mathf.Clamp(vNow / BusVoltage, -1f, 1f);
                BatteryCurrent = vNow * Current / BusVoltage;

                float friction = kt * motor.no_load_current_a * (float)System.Math.Tanh(omega / 20f);
                sum += kt * Current - friction;
            }
            Torque = sum / n;
            return Torque;
        }
    }
}
