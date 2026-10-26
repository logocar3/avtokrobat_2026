// A car file (cars/<name>.json, the reference is cars/udirc_1610s.json), as read by the
// simulator (JsonUtility: field names must match the JSON; missing fields keep the defaults
// below, which are the UDIRC 1610S values). Students build their own car by copying the
// reference and changing it; CarLibrary.cs loads, checks and summarises car files, and reads
// encrypted mystery cars (*.car).
namespace Avtokrobat
{
    [System.Serializable]
    public class UdircGeometry
    {
        public float length = 0.292f, width = 0.225f, height = 0.127f;
        public float wheelbase = 0.180f, track = 0.180f;
        public float tire_radius = 0.0425f, tire_width = 0.045f, ground_clearance = 0.024f;
    }

    [System.Serializable]
    public class UdircMotorModel : MotorSpec
    {
        public float gear_ratio = 11.4f;               // motor turns per wheel turn
    }

    // The simulation model of the car (UdircSim): mass, inertia, suspension, tyre, drivetrain.
    [System.Serializable]
    public class UdircUnityModel
    {
        public float mass_kg = 1.35f;                  // whole car, with battery
        public float wheel_mass_kg = 0.045f;           // one wheel + tyre
        public float[] inertia_rpy_kgm2 = { 0.0075f, 0.0114f, 0.0153f };   // roll, pitch, yaw about COM
        public float com_height_m = 0.010f;            // above the axle line
        public float com_forward_m = 0.0f;             // ahead of mid-wheelbase
        public float suspension_travel_m = 0.025f;
        public float spring_n_per_m = 400f;            // per wheel, at the wheel
        public float damper_ns_per_m = 9f;             // per wheel, at the wheel
        public float max_steer_deg = 30f;
        public float servo_rate_deg_s = 600f;          // ~0.1 s / 60 deg hobby servo
        public float gear_efficiency = 0.9f;
        public string motor_axis = "longitudinal";     // or "transverse" (parallel to the axles)
        public string drivetrain = "4wd";              // "4wd" (locked centre shaft), "rwd" or "fwd"
        public string front_diff = "open";             // "open" or "locked" (spool), driven axles only
        public string rear_diff = "open";
        public float launch_speed_m_s = 7.0f;          // scripted run: speed target to the lip
        // sensor mounts, body frame (x fwd, y left, z up) from mid-wheelbase on the axle line
        public float[] accel_mount_xyz = { 0f, 0f, 0.01f };
        public float[] lidar_mount_xyz = { 0.146f, 0f, 0.03f };
        // braking (speed mode + keyboard): torque that opposes the motion is capped at this
        // fraction of the VESC's max torque; the keyboard reverses only once (nearly) stopped
        public float brake_torque_fraction = 0.45f;
        public float reverse_torque_fraction = 0.5f;
        public UdircTire tire = new UdircTire();
    }

    // Tyre model (UdircSim): Pacejka-style F = mu N sin(C atan(B s)) for the slip ratio
    // (longitudinal) and the slip angle (lateral), combined on a friction circle.
    [System.Serializable]
    public class UdircTire
    {
        public float mu = 1.1f;                // the tyre's own grip; a run uses min(this, surface)
        public float Bx = 12f, Cx = 1.4f;      // longitudinal: peak at slip ~0.17, sliding ~0.8 of peak
        public float By = 15f, Cy = 1.3f;      // lateral: peak at ~10 deg slip angle
        public float v_reg = 0.7f;             // m/s, slip denominator floor (low-speed regularisation)
        public float rolling_resistance = 0.015f;   // coefficient (x normal load)
        public int substeps = 20;              // wheel-spin integration steps per 1 ms
        public float diff_damping = 0.005f;    // N m s/rad against the left/right speed difference (diff friction)
    }

    [System.Serializable]
    public class UdircFile
    {
        public string name = "", description = "";
        public UdircGeometry geometry_m = new UdircGeometry();
        public UdircMotorModel motor_model = new UdircMotorModel();
        public BatterySpec battery = new BatterySpec();
        public VescSpec vesc = new VescSpec();
        public UdircUnityModel unity_model = new UdircUnityModel();   // loaded by SimConfig.LoadCar
    }
}
