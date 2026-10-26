# Identification experiments: student guide

You get a simulated RC car: Project Chrono physics, simulated on-board sensors, and the car's
own controller. Your job is to **design experiments that identify it**. An experiment is one
small JSON file. You set the starting state, the input signals and the duration, run it, and
get CSV files of what the car commanded and measured.

```
experiment.json ──► simulator (physics + sensors + on-board controller) ──► internal.csv, external.csv, truth.csv
```

## 1. Setup (once)

1. Install **Docker Desktop** and start it. Wait until it says it is running.
2. Get the simulator image (large, about 27 GB, so do it on a good connection):
   `docker pull uwsbel/projectchrono:latest`
3. Open PowerShell in the `Simulation` folder.

## 2. Run an experiment

```powershell
.\docker\run-experiment.ps1 experiments\01_torque_step.json     # one experiment
.\docker\run-experiment.ps1 experiments                         # every .json in the folder
.\docker\run-experiment.ps1 my_tests\a.json my_tests\b.json     # your own files (inside Simulation\)
```
Results go to `output\experiments\<file name>\`. A run takes about a third of the simulated
time. Runs are **deterministic**: the same file with the same `seed` always gives the same CSVs.
Change `seed` to get a new noise realisation.

To *watch* the car, use the Unity viewer's live mode (see the main README). The experiment
files themselves run headless.

## 3. The example experiments

| File | What it excites | Typical use |
|---|---|---|
| `01_torque_step.json` | torque step from rest, then coast | motor-torque → speed model, drag/rolling losses |
| `02_speed_staircase.json` | closed-loop speed steps (on-board PI) | speed-loop response, steady-state gains |
| `03_prbs_torque.json` | PRBS torque around an operating point | black-box (ARX/OE/...) identification |
| `04_steering_chirp.json` | steering chirp 0.2 → 3 Hz at a constant 3 m/s | lateral/yaw dynamics, steering frequency response |
| `05_airborne_wheel_torque.json` | thrown up; wheels spun up and braked in the air | in-air pitch reaction, wheel/chassis inertia ratio |
| `06_jump.json` | the ramp jump | the full launch–flight–landing sequence |
| `07_udirc_jump_speed.json` | the ramp jump in speed mode (Unity engine) | the jump with the light UDIRC car, which overshoots at full torque |
| `08_obstacle_course.json` | slalom, bump, plank, ice (Unity engine) | demo of a custom environment (`environments/obstacle_course.json`) |
| `09_mystery_identification.json` | torque steps, coast-down, steering step on the **mystery car** (Unity) | identify an unknown car from sensors only (section 11) |
| `10_my_car_jump.json` | the jump with a student-built car (Unity) | test your own car (section 10) |

Copy one and change it. That's the intended workflow.

> **Two engines.** Every file runs on Chrono (`docker\run-experiment.ps1`, the RCCar stand-in)
> and on the Unity engine (`run-unity-experiment.ps1`, the real UDIRC 1610S). See sections 8
> and 9. The numbers differ, because the cars differ.

## 4. Experiment file reference

Every field is optional except `inputs`. The defaults are shown here; JSON has no comments,
so the notes are on the right.

```jsonc
{
  "description": "",                 // free text
  "scene": "flat",                   // "flat" (200 m x 200 m ground) or "jump" (ramp + gap + landing)
  "duration": 5.0,                   // simulated seconds
  "seed": 0,                         // sensor-noise seed

  "initial_state": {
    "position": [0, 0, null],        // chassis reference point [m]; z = null → wheels on the ground
    "yaw_deg": 0, "pitch_deg": 0, "roll_deg": 0,
    "speed": 0.0,                    // forward speed [m/s], wheels already rolling to match
    "velocity": [0, 0, 0],           // extra world-frame velocity [m/s], e.g. [0, 0, 4] = thrown up
    "angular_velocity_deg_s": [0, 0, 0]   // body frame: x roll, y pitch, z yaw [deg/s]
  },

  "inputs": {
    "motor_mode": "torque",          // "torque": motor torque [N m], or "speed": motor speed target [rad/s]
    "motor": 0.0,                    // a signal (section 5)
    "steering": 0.0                  // a signal: steering angle [rad], + = left
  },

  "sensors": { "noise": true },      // false = noise-free sensors (for checking your method)
  "outputs": {
    "truth": true,                   // write truth.csv (your teacher may switch this off)
    "rate_hz": 200,                  // internal.csv / external.csv sample rate
    "truth_rate_hz": 50
  },
  "stop_if_off_world": true          // stop if the car drives off the edge of the ground
}
```

**Coordinates and signs.** World frame: x forward (the ramp is at +x), y left, z up.
Body frame: x forward, y left, z up.

| Quantity | Sign convention |
|---|---|
| `pitch` | rotation about +y; **positive = nose down** (the CSVs use the same sign) |
| `roll` | positive = left side up |
| `yaw` | positive = turning left |
| `steering` | positive = left |
| `motor` | positive = forward |

**Actuator limits** (the controller clips to these):
- Motor torque: ±0.122 N·m. A **negative torque brakes**; there is no reverse.
- Motor speed: 0 to about 2000 rad/s in speed mode.
- Steering: ±0.436 rad (±25°), with the servo slewing at about 600°/s.
- The controller updates every 5 ms (200 Hz) and holds its command between updates.

## 5. Signals

You can use a signal anywhere a value is expected (`inputs.motor`, `inputs.steering`):

| Form | Meaning |
|---|---|
| `0.06` | constant |
| `[[0, 0], [1, 0.05], [3, 0.05]]` | piecewise linear through `[t, value]` points |
| `{"type": "points", "points": [[t, v], ...], "interp": "hold"}` | piecewise **constant** (staircase) |
| `{"type": "step", "t0": 0.5, "value": 0.06, "before": 0, "t1": 2.5, "after": 0}` | step (a pulse if `t1` is given) |
| `{"type": "ramp", "t0": 1, "t1": 3, "v0": 0, "v1": 0.1}` | linear ramp |
| `{"type": "sine", "amp": 0.1, "freq": 1.0, "offset": 0, "phase_deg": 0, "t0": 0, "t1": 5}` | sine inside [t0, t1] |
| `{"type": "chirp", "amp": 0.1, "f0": 0.2, "f1": 3, "t0": 1, "t1": 9, "method": "linear"}` | swept sine (`"log"` also available) |
| `{"type": "prbs", "amp": 0.03, "offset": 0.05, "period": 0.1, "t0": 0.5, "t1": 6, "seed": 7}` | pseudo-random binary ±amp, switching every `period` s |
| `{"type": "sum", "terms": [signal, signal, ...]}` | sum of any of the above |

Outside their `[t0, t1]` window, `sine`, `chirp` and `prbs` return their `offset`.

## 6. Output files

**`internal.csv`**: what the on-board controller *sent* (stamped when sent).

| Column | Unit | Meaning |
|---|---|---|
| `t` | s | time |
| `steering_cmd` | −1..1 | servo command (+ = left) |
| `servo_angle_cmd` | rad | commanded steering angle |
| `esc_cmd` | −1..1 | ESC command: > 0 drive, < 0 brake |
| `throttle_cmd`, `brake_cmd` | 0..1 | the two halves of `esc_cmd` |
| `torque_cmd` | N·m | requested motor torque |

**`external.csv`**: what the car *measured*.

| Column | Unit | Meaning |
|---|---|---|
| `acc_x`, `acc_y`, `acc_z` | m/s² | accelerometer: body-frame specific force. Reads (0, 0, +9.81) at rest and about 0 in free fall; 50 Hz low-pass plus noise |
| `hall_a`, `hall_b`, `hall_c` | 0/1 | the three Hall sensors of the motor |
| `hall_state` | 0..5 | electrical sector |
| `hall_count` | steps | signed commutation-step counter. 1 step = 60° electrical = 30° of motor shaft |
| `motor_pos_hall` | rad | motor angle from the step count (30° resolution) |
| `motor_speed_hall` | rad/s | motor speed from steps per sample (coarse: about 105 rad/s per step at 200 Hz) |
| `lidar_frame` | – | increments with each new lidar frame (15 Hz). Values are held in between |
| `lidar_r{row}c{col}` | m | 8×8 front distance sensor, 45° field of view, 4 m range. Row 0 = top, col 0 = left. `nan` = no return |

The motor turns **11.4 times per wheel revolution**. The tire radius is 0.085 m.

**`truth.csv`**: the true state, for checking your model. You couldn't measure these on the real car.

| Column | Meaning |
|---|---|
| `x`, `y`, `z` | position [m] |
| `roll`, `pitch`, `yaw` | attitude [rad] |
| `vx`, `vy`, `vz` | velocity [m/s, world] |
| `wx`, `wy`, `wz` | angular rate [rad/s, body] |
| `terrain_h`, `clearance` | ground height and height above it [m] |
| `throttle`, `braking`, `steering` | inputs actually applied (after the servo) |
| `motor_angle`, `motor_speed` | true motor shaft angle [rad] and speed [rad/s] |

**`experiment.json`** is the experiment exactly as it was run, with all defaults filled in.
Keep it with your data.

## 7. Good to know

- **The sensors are realistic, not perfect.** There's noise, quantisation (Hall), sample-and-hold
  (lidar at 15 Hz) and filtering (accelerometer). Repeat experiments with different `seed`s
  and see how your estimates spread.
- **Excite what you want to identify:** steps for gains and time constants, chirps for frequency
  response, PRBS for black-box models. Keep the car on the ground for driving experiments.
  The flat ground is 200 m × 200 m, and the run stops with a note if the car leaves it.
- **Hard or strongly tilted landings** (more than about 15° nose-up or nose-down from above
  about 1 m) can bounce unrealistically, because the simulated suspension has no bump stops.
  In-air experiments should end before touchdown, as `05` does. If the simulation ever becomes
  unstable, the run stops and says so.
- **The braked car at rest shows about ±2 m/s² of vibration on `acc_x`.** It comes from
  contact micro-vibration. Treat it as sensor/vehicle vibration noise.
- The simulated vehicle is a **stand-in** for the real UDIRC 1610S. It's larger and heavier
  (about 0.47 m wheelbase, 6.8 kg), but the sensors and the controller interface are those of
  the real car. The methods you develop carry over; the numbers you identify belong to the
  stand-in.

## 8. The Unity engine: the real UDIRC 1610S, no Docker

The same experiment files also run in Unity. The car there is the **UDIRC 1610S** itself:
0.18 m wheelbase, 1.40 kg, 85 mm tyres and a 2845 4700KV motor on 3S through a VESC in
torque (current) control, with a 2 ms command delay and a 1 ms current loop. The on-board
controller and the physics both run at **1 kHz**. The car's numbers live in
`cars/udirc_1610s.json`.

**Run headless** from the `Simulation` folder (no Docker; the Unity editor may stay open):

```powershell
.\run-unity-experiment.ps1 experiments\07_udirc_jump_speed.json   # one experiment
.\run-unity-experiment.ps1 experiments                             # every .json in the folder
```

Results go to `output\unity_experiments\<name>_<yyyyMMdd-HHmmss>\`, a new folder for every run, so nothing is overwritten. A run takes well under a second; the first
call of the day spends a minute or two importing the project. If Unity isn't installed in the
default Hub folder, add `-UnityExe "<path>\Unity.exe"`.

**Run and watch** in Unity: press Play, choose **Unity physics - PhysX**, and use the
**Experiments** panel. Pick a file, then **Run (watch)** (at the rate you choose, which you can
pause) or **Run (fast)**. It writes the same CSVs. In the editor without Play mode, use the
menu **Avtokrobat > Run Experiment File...** or **Run All Experiments**.

**What's different from Chrono:**

| | Chrono | Unity |
|---|---|---|
| car | RCCar stand-in, 6.8 kg, 0.47 m wheelbase | UDIRC 1610S, 1.40 kg, 0.18 m wheelbase, 42.5 mm tyre radius |
| control rate | `control_every` × 1 ms | every 1 ms step |
| motor | normalised throttle/brake | VESC torque control: delay, current loop, voltage and current limits |
| negative torque | brake (no reverse) | reverse torque. To brake to a stop, use speed mode with target 0 |
| `scene` | `flat` or `jump` | `flat`, `jump`, or any environment file (section 9) |
| lidar mount | 0.36 m ahead of the COG | front bumper, 0.146 m ahead of mid-wheelbase, about 7 cm up |

The CSV columns are the same. `internal.csv` has extra VESC columns at the end:
`vesc_torque_seen` (the request after the command delay), `current_ref_a`, `current_a`, `duty`,
`bus_voltage`, `battery_current_a` and `shaft_torque`. In `truth.csv`, `x, y, z` is
mid-wheelbase on the axle line.

**Extra experiment fields (Unity only):**

```jsonc
{
  "scene": "obstacle_course",                    // an environment file name, or "flat" / "jump"
  "environment": { "pieces": [ ... ] },          // or an inline environment (wins over "scene")
  "car": "udirc_1610s",                          // a car from cars/ (section 10), or a mystery car "x.car" (section 11)
  "car_overrides": { "unity_model": { "mass_kg": 1.6 },   // any part of the car file (not for mystery cars)
                     "vesc": { "command_delay_s": 0.005 } },
  "tire_mu": 0.9                                 // tyre grip (default 1.1)
}
```

## 9. Building an environment (Unity engine)

An environment is a list of **pieces** in the experiment frame (x forward, y left, z up, metres).
Put it in `experiments/environments/<name>.json` and use `"scene": "<name>"`. In the Unity
PhysX mode it also appears in the **Environment** picker. Press **Reset** to reload it after
you edit the file.

```json
{
  "description": "my track",
  "start": { "position": [0, 0, null], "yaw_deg": 0 },
  "pieces": [
    { "type": "ground", "x": [-3, 10], "width": 4 },
    { "type": "ramp",   "x": [3, 3.54], "z": [0, 0.25], "width": 1.2, "color": [0.85, 0.5, 0.35] },
    { "type": "ramp",   "x": [4.5, 5.5], "z": [0.25, 0], "width": 1.2 },
    { "type": "box",    "center": [8, 0.5, 0.05], "size": [0.1, 0.1, 0.1], "yaw_deg": 30 }
  ]
}
```

| Piece | Required | Optional (default) |
|---|---|---|
| `ground`: flat slab, top at `z` | `x: [x0, x1]` | `width` (200), `y` (0), `z` (0), `thickness` (0.5), `yaw_deg` |
| `ramp`: slope from (x0, z0) to (x1, z1) | `x: [x0, x1]`, `z: [z0, z1]` | `width` (8), `y` (0), `bottom` (min(z0, z1) − 0.5), `yaw_deg` |
| `box`: obstacle | `center: [x, y, z]`, `size: [lx, ly, lz]` | `yaw_deg`, `pitch_deg` (+ = nose down), `roll_deg` (+ = left up) |

Every piece also takes `name`, `color: [r, g, b]` (0–1) and `mu`, the grip of that surface. The
tyre gets min(tyre μ, piece μ), so `"mu": 0.15` makes an ice patch.

- **A ramp is one piece for anything sloped:** an up-ramp (`z: [0, 0.3]`), a landing
  (`z: [0.3, 0]`), a table top (`z: [0.3, 0.3]`), or a bump (two short ramps).
  For an angle θ and lip height h, the length is h / tan θ (15° and 0.3 m give 1.12 m).
- **Pieces may overlap.** The car drives on the top-most surface. Put a ground slab under
  everything so the car can't fall between pieces.
- **The car is small:** 24 mm ground clearance and a 42.5 mm tyre radius. Edges taller than
  about 2 cm without a ramp stop it, as they would stop the real car.
- The run stops (`stop_if_off_world`) when the car falls 2 m below the lowest ground or ramp.
- `start` is only used by the interactive drive mode. Experiments use `initial_state`.
- Examples: `environments/jump.json` (the M1 jump), `flat.json`, `small_kicker.json` (a jump
  sized for the 1/16 car) and `obstacle_course.json` (cone gates, a bump, a tilted plank, ice).

## 10. Building your own car (Unity simulator)

A car is one JSON file in `cars/`. The reference is `cars/udirc_1610s.json`, the real UDIRC
1610S. To build your own car, copy it to `cars/<my_car>.json` and change the numbers. Then:

- **in the simulator**, pick it in the **Car** list (press **Reset** after you edit the file);
- **in an experiment**, use `"car": "my_car"` (see `experiments/10_my_car_jump.json`).

The 3D body scales to your car's wheelbase, track and wheel size. The simulator checks the file
when it loads. **Errors** stop the run and say what's wrong: a negative mass, an unknown
drivetrain, or an inertia no rigid body can have. **Warnings** run anyway but flag numbers that
look odd: an inertia far from a solid box of the car's size, unusual suspension frequency or
damping, very heavy wheels. Tick **Spec sheet** in the panel to see what your numbers imply: top
speed, peak acceleration (motor-limited or grip-limited), suspension frequency and damping,
turning radius, rollover threshold, and how strongly spinning the wheels rotates the car in the
air.

**What you can change:**

| Section | Fields |
|---|---|
| `name`, `description` | shown in the car list |
| `geometry_m` | `wheelbase`, `track`, `tire_radius`, `tire_width`, `length`, `width`, `height`, `ground_clearance` (m) |
| `motor_model` | `kv_rpm_per_v`, `resistance_ohm`, `no_load_current_a`, `rotor_inertia_kgm2`, `max_current_a`, `gear_ratio` (motor turns per wheel turn), `pole_pairs` (Hall sensor) |
| `battery` | `cells`, `cell_voltage` (open circuit, 3.7 nominal / 4.2 full), `internal_resistance_ohm` |
| `vesc` | `command_delay_s`, `current_time_constant_s`, `motor_current_max_a` / `_min_a`, `battery_current_max_a` / `_min_a`, `max_duty` |
| `unity_model` | `mass_kg` (whole car), `wheel_mass_kg` (one wheel + tyre), `inertia_rpy_kgm2` [roll, pitch, yaw] about the centre of mass, `com_height_m` (above the axle line), `com_forward_m` (ahead of mid-wheelbase) |
| | `suspension_travel_m`, `spring_n_per_m`, `damper_ns_per_m` (per wheel) |
| | `drivetrain`: `"4wd"` (locked centre shaft), `"rwd"`, `"fwd"`; `front_diff` / `rear_diff`: `"open"` or `"locked"` |
| | `max_steer_deg`, `servo_rate_deg_s`, `gear_efficiency`, `motor_axis` (`"longitudinal"` / `"transverse"`) |
| | `brake_torque_fraction`, `reverse_torque_fraction` (speed-mode / keyboard braking), `launch_speed_m_s` (scripted run) |
| | `accel_mount_xyz`, `lidar_mount_xyz`: sensor positions [x fwd, y left, z up] from mid-wheelbase on the axle line |
| `unity_model.tire` | `mu` (grip; a run uses min(tyre, surface)), `Bx`, `Cx`, `By`, `Cy` (slip-curve shape), `rolling_resistance`, `diff_damping` |

Fields you leave out keep the UDIRC 1610S value. Fields whose names start with `_` are comments.

## 11. The mystery car: identify a car you can't see inside

Your teacher may give you a **mystery car**: a file `cars/<name>.car`. It's a normal car with
unknown numbers. You can drive it and run experiments with it (`"car": "<name>.car"`; see
`experiments/09_mystery_identification.json`), but:

- **the simulator shows no parameters** and no true state (pose, speed, attitude). The panel shows
  only what the car itself measures and reports: accelerometer, Hall speed, lidar frame, and
  VESC current, duty and battery;
- **experiments write no `truth.csv`**, only `internal.csv` and `external.csv`, as on the real car;
- **`car_overrides` and `tire_mu` are refused**, so you can't swap in a known value.

Your job is to design experiments that reveal its parameters: mass, motor constant and gear
ratio, current limits, delays, drag, grip, steering response, inertia. Then check your model by
predicting a new experiment before you run it. The geometry shows in the 3D view, so treat it
as measured, as you would with a ruler on a real car.
