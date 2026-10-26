# Walkthrough: install, run, modify

This walkthrough takes you from an empty Windows PC to running the simulator, running
experiments, and building your own experiments, tracks and cars. It takes about 30 minutes,
most of it waiting for Unity to install.

You need **Unity only**. Docker and Chrono, which the rest of this repository mentions, are an
older engine and aren't needed for anything below.

**Contents:** 1. Install · 2. First run · 3. Run an experiment · 4. Look at the results ·
5. Write your own experiment · 6. Build a track · 7. Build a car · 8. The mystery car ·
9. Change the simulator itself · 10. Teachers · 11. Troubleshooting · 12. Folder map

---

## 1. Install

### 1.1 Unity

1. Install **Unity Hub** from <https://unity.com/download>.
2. In Unity Hub, go to **Installs → Install Editor** and install **Unity 6000.5.2f1**. Pick it from
   the *Archive* tab if it isn't listed. You don't need any extra modules (no Android, iOS or
   WebGL). Keep the default install folder: the experiment script looks for
   `C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe`.

> Another Unity 6 version will probably work too, but this is the version the project was
> built and tested with.

### 1.2 The project

Copy or unzip the `Simulation` folder anywhere, for example `D:\Avtokrobat\Simulation`.
**Keep its inner layout as it is.** The simulator finds the car, track and experiment files by
going three folders up from `Unity\AvtokrobatViz\Assets`, so `Unity`, `cars`, `config` and
`experiments` must stay side by side inside `Simulation`.

### 1.3 Open it once in Unity

1. In Unity Hub: **Projects → Add → Add project from disk**, and choose
   `Simulation\Unity\AvtokrobatViz`.
2. Open it. **The first open takes a few minutes** while Unity imports and compiles. That only
   happens once.
3. If Unity asks about the editor version, choose to open with 6000.5.2f1.

### 1.4 PowerShell (for the experiment script)

Windows blocks downloaded scripts by default. Allow local scripts once, for your user only:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

If you got the project as a **downloaded zip**, Windows also marks every file as "from the
internet". Unmark the scripts once, from the `Simulation` folder:

```powershell
Get-ChildItem -Recurse -Filter *.ps1 | Unblock-File
```

If you'd rather not change any of that, run the script as
`powershell -ExecutionPolicy Bypass -File .\run-unity-experiment.ps1 ...`.

### 1.5 Python (optional, for analysing results)

The results are plain CSV files, so any tool works (Excel, MATLAB, Python). For the Python
examples below, install Python 3 and `pip install pandas matplotlib`.

---

## 2. First run

1. In Unity, press **▶ Play** at the top. The simulator starts by itself, with no scene setup.
2. On the left there is a panel. From the top:
   - **Car:** which car to drive. `udirc_1610s` is the real UDIRC 1610S.
   - **Environment:** which track. `jump` is a 15° ramp, a gap and a landing slope.
   - **Simulation:** Run / Pause, Step 10 ms, Reset, and the rate (0.1x – 2x).
   - **Driver:** *Scripted* drives the jump by itself. *Keyboard* lets you drive.
   - **Parameters:** sliders for mass, grip, suspension, VESC delay and so on. They're applied
     on **Reset**, and **From files** puts them back.
   - **Experiments:** runs experiment files (section 3).
   - **Telemetry:** position, speed, attitude, sensors, and the controller and VESC state.
3. Press **Run**. The car accelerates, jumps and lands. The *First jump* box shows launch speed,
   apex height, air time and landing point.
4. Switch the driver to **Keyboard**, press **Reset**, then **Run**:
   - **W** full throttle;
   - **S** brake, then reverse once the car has stopped;
   - **A / D** steer.

   Pick the `flat` or `obstacle_course` environment for more room.

**Camera:** drag with the left mouse button (outside the panel) to orbit, use the wheel to zoom,
and keys **1–4** for side, behind, front ¾ and top views. **Space** runs or pauses, **R** resets
and runs.

Physics and the car's controller both run at **1 kHz** (every 1 ms). At rate 1x the simulation
runs in real time.

---

## 3. Run an experiment

An **experiment** is one small JSON file in `experiments\`. It sets the car, the track, the start
state and the input signals, and its result is a set of CSV files. There are three ways to run one.

**In Play mode, while you watch.** In the **Experiments** panel, pick a file (for example
`01_torque_step`) and press **Run (watch)**. You can pause it and change the rate. **Run (fast)**
runs as fast as possible. **Back to driving** returns to free driving.

**Headless, from PowerShell** (no Play mode needed, and Unity may stay open). From the
`Simulation` folder:

```powershell
.\run-unity-experiment.ps1 experiments\01_torque_step.json     # one experiment
.\run-unity-experiment.ps1 experiments                         # every experiment in the folder
```

The first call takes a minute or two while it prepares a private copy of the project in
`Unity\_runner\`. After that, each experiment takes about a second. The script prints one line
per experiment.

**In the Unity editor without Play:** menu **Avtokrobat → Run Experiment File...** or
**Run All Experiments**.

The examples:

| File | What it does |
|---|---|
| `01_torque_step` | a torque step from rest, then coasting |
| `02_speed_staircase` | closed-loop speed steps |
| `03_prbs_torque` | a pseudo-random torque signal (for black-box identification) |
| `04_steering_chirp` | a steering sweep at constant speed |
| `05_airborne_wheel_torque` | the car is thrown up and spins its wheels in the air |
| `06_jump`, `07_udirc_jump_speed` | the jump (06 uses full torque and overshoots; 07 is tuned) |
| `08_obstacle_course` | a slalom, a bump, a plank and an ice patch |
| `09_mystery_identification` | the mystery car (section 8) |
| `10_my_car_jump` | the jump with a student-built car (section 7) |

---

## 4. Look at the results

Every run writes a **new folder**, `output\unity_experiments\<name>_<date>-<time>\`. Nothing is
ever overwritten. In Play mode, **Open results in folder** opens it.

| File | What's in it |
|---|---|
| `internal.csv` | what the car's controller **sent**: steering, torque request, plus the VESC's own telemetry (current, duty, battery voltage and current) |
| `external.csv` | what the car **measured**: accelerometer, Hall motor sensor (state, step count, speed), 8×8 lidar |
| `truth.csv` | the **true** state (position, attitude, velocities, motor speed), for checking your model. It isn't written for mystery cars |
| `experiment.json` | the experiment exactly as it was run, with defaults filled in |

All files use **x forward, y left, z up** (metres, seconds, radians). The full column reference
is in [experiments/README.md](experiments/README.md) §6 and §8.

A first plot in Python:

```python
import glob, pandas as pd, matplotlib.pyplot as plt
run = sorted(glob.glob(r"output\unity_experiments\01_torque_step_*"))[-1]   # newest run
cmd   = pd.read_csv(run + r"\internal.csv")
sens  = pd.read_csv(run + r"\external.csv")
truth = pd.read_csv(run + r"\truth.csv")

fig, ax = plt.subplots(3, 1, sharex=True)
ax[0].plot(cmd.t, cmd.torque_cmd);           ax[0].set_ylabel("torque cmd [N m]")
ax[1].plot(sens.t, sens.motor_speed_hall);   ax[1].set_ylabel("Hall speed [rad/s]")
ax[2].plot(truth.t, truth.vx);               ax[2].set_ylabel("true speed [m/s]")
ax[2].set_xlabel("t [s]"); plt.show()
```

---

## 5. Write your own experiment

Copy an example and change it. That's the intended workflow. For example, save this as
`experiments\my_step.json`:

```json
{
  "description": "my first experiment: a half-torque step, then a steering step",
  "car": "udirc_1610s",
  "scene": "flat",
  "duration": 5.0,
  "initial_state": { "position": [0, 0, null], "speed": 0.0 },
  "inputs": {
    "motor_mode": "torque",
    "motor":    { "type": "step", "t0": 0.5, "value": 0.06, "t1": 3.0, "after": 0 },
    "steering": { "type": "step", "t0": 2.0, "value": 0.1 }
  }
}
```

Run it with `.\run-unity-experiment.ps1 experiments\my_step.json`, or press **Rescan** in the
Experiments panel and pick it.

- **`motor_mode`** is `"torque"` (the motor signal is a torque in N·m, at most ±0.122 for the
  stock motor) or `"speed"` (the motor signal is a target motor speed in rad/s, held by the
  on-board PI controller).
- **`steering`** is a steering angle in rad, + = left.
- **Signals** can be a number, a list of `[t, value]` points, or `step`, `ramp`, `sine`,
  `chirp`, `prbs` or `sum` objects ([experiments/README.md](experiments/README.md) §5).
- **`initial_state`** can also give a pose, a forward speed, a throw velocity or a spin
  (§4 of the same guide).
- **Sensor noise:** `"sensors": { "noise": false }` turns it off. `"seed"` picks a different
  noise realisation.

---

## 6. Build a track (environment)

A track is a list of pieces in a JSON file in `experiments\environments\`, in metres, with x
forward, y left and z up:

```json
{
  "description": "my track",
  "start": { "position": [0, 0, null], "yaw_deg": 0 },
  "pieces": [
    { "type": "ground", "x": [-3, 12], "width": 4 },
    { "type": "ramp",   "x": [3, 3.54], "z": [0, 0.25], "width": 1.2, "color": [0.85, 0.5, 0.35] },
    { "type": "ramp",   "x": [4.5, 5.5], "z": [0.25, 0], "width": 1.2 },
    { "type": "ground", "x": [7, 9], "width": 4, "z": 0.002, "thickness": 0.01, "mu": 0.15, "color": [0.75, 0.9, 1] },
    { "type": "box",    "center": [10, 0.5, 0.05], "size": [0.1, 0.1, 0.1], "yaw_deg": 30 }
  ]
}
```

- `ground` is a flat slab, `ramp` is any slope from (x0, z0) to (x1, z1), and `box` is an obstacle.
- `mu` sets a surface's grip: 0.15 is ice. `color` is RGB from 0 to 1.
- **To use it:** in Play mode, pick it in the **Environment** list. Edit the file and press
  **Reset** to see the change at once. In an experiment, use `"scene": "<file name>"`.
- **The car is small:** 24 mm ground clearance and a 42.5 mm wheel radius. Edges taller than
  about 2 cm stop it unless there's a ramp, as on the real car.

Full reference: [experiments/README.md](experiments/README.md) §9. Examples are in
`experiments\environments\`.

---

## 7. Build a car

A car is one JSON file in `cars\`. The reference `cars\udirc_1610s.json` is the real UDIRC 1610S.

1. Copy it to `cars\my_car.json`.
2. Change `name`, `description` and any numbers. Fields you leave out keep the 1610S value. Some
   examples:
   - `unity_model.mass_kg`, `inertia_rpy_kgm2`, `com_height_m`, `com_forward_m`
   - `motor_model.kv_rpm_per_v`, `gear_ratio`, `battery.cells`
   - `unity_model.drivetrain`: `"4wd"`, `"rwd"` or `"fwd"`; `rear_diff`: `"open"` or `"locked"`
   - `unity_model.tire.mu` (grip), `spring_n_per_m`, `damper_ns_per_m`, `max_steer_deg`
3. **Try it.** In Play mode, pick `my_car` in the **Car** list (press **Reset** after each edit)
   and tick **Spec sheet**. It shows top speed, peak acceleration and whether it's limited by
   the motor or by grip, suspension frequency and damping, turning radius, rollover limit, and
   how strongly wheel spin rotates the car in the air.
4. **Test it.** Use `"car": "my_car"` in an experiment. See `experiments\10_my_car_jump.json`,
   which uses `cars\example_student_car.json`, a rear-drive variant.

When it loads a car, the simulator **checks it**:

- **errors** stop it, with a message that says what's wrong (for example "mass must be > 0" or
  "inertia is not a possible rigid body");
- **warnings** let it run but point out odd numbers (for example an inertia far from a solid box
  of the car's size).

Every field is listed in [experiments/README.md](experiments/README.md) §10.

---

## 8. The mystery car

Your teacher may give you a mystery car, `cars\<name>.car`: a car with unknown numbers. You can
drive it (pick it in the **Car** list) and run experiments with it (`"car": "<name>.car"`; see
`09_mystery_identification.json`). But:

- the panel shows only what the car itself measures: accelerometer, Hall speed, lidar, and the
  VESC's current, duty and battery. It shows no parameters and no true position or speed;
- experiments write **no `truth.csv`**;
- `car_overrides` and `tire_mu` are refused.

Your task is to design experiments that reveal its mass, motor constant, gear ratio, current
limits, delays, drag, grip and steering response. Then check your model by predicting an
experiment before you run it. The guide is [experiments/README.md](experiments/README.md) §11.

---

## 9. Change the simulator itself

The simulator is C# in `Unity\AvtokrobatViz\Assets\Scripts\`. Edit a file in any editor (VS Code
or Visual Studio). When you switch back to Unity it recompiles in a few seconds, and errors appear
in Unity's **Console** window.

| To change ... | Look in |
|---|---|
| the car's physics (suspension, drivetrain, tyres, spin reaction) | `UdircSim.cs` |
| the on-board controller (torque / speed modes; add your own, for example an in-air attitude controller) | `UdircController.cs` |
| the motor controller (delay, current loop, limits) | `VescModel.cs` |
| the sensors (accelerometer, Hall, lidar) | `UnitySensors.cs` |
| the car-file format, checks and spec sheet | `UdircSpec.cs`, `CarLibrary.cs` |
| track pieces | `EnvironmentBuilder.cs` |
| experiment files and signals | `ExperimentRunner.cs`, `Signals.cs` |
| the panel, driving and camera | `UnityPhysicsController.cs` |
| the car's look | `SceneBuilder.cs` |

**After a change, check that nothing broke** by running all the examples headless:

```powershell
.\run-unity-experiment.ps1 experiments
```

They're deterministic: the same files give the same results. Comparing the printed lines before
and after a change is a quick regression test. A `FAILED` line or an `error CS...` line tells you
what broke. The full log is `output\unity_experiments\unity_batch.log`.

A few rules that save time:

- Coordinates in files and CSVs are x forward, y left, z up. Unity is y up. All conversion goes
  through `FrameConvert.cs`.
- The car files are read with Unity's `JsonUtility`: **a C# field name must match the JSON key
  exactly**, or the value is silently ignored. If you add a field to `UdircSpec.cs`, add it to
  `cars\udirc_1610s.json` and the table in `experiments\README.md` §10.
- There is no version control (git) in this folder. Copy a file before a big edit.

---

## 10. For teachers

- **Make a mystery car:** write a car JSON into `teacher\`, then run
  `.\teacher\make-mystery-car.ps1 teacher\<name>.json`. This writes `cars\<name>.car`.
  Details are in [teacher/README.md](teacher/README.md).
- **Hand out** the whole `Simulation` folder **except `teacher\`** (the answers) and `output\`.
- The encryption keeps the numbers out of sight, but the key is in `CarLibrary.cs`, so it won't
  stop a student who reads the source.
- **Per-experiment changes for an exercise**, for example a heavier car or a slower VESC:
  `"car_overrides": { "unity_model": { "mass_kg": 1.6 }, "vesc": { "command_delay_s": 0.005 } }`.

---

## 11. Troubleshooting

| Problem | Fix |
|---|---|
| `Unity not found at C:\Program Files\...` | Unity is installed elsewhere. Add `-UnityExe "<path>\Unity.exe"` to the script call |
| `...cannot be loaded because running scripts is disabled` | see section 1.4 |
| The panel says *config/scene.json not found*, or cars / environments are missing | the folder layout changed. `Unity`, `cars`, `config` and `experiments` must sit together inside `Simulation` (section 1.2) |
| The first script run is slow | it's preparing `Unity\_runner\`. Later runs are fast |
| `FAILED <file>: ...` | the message says what's wrong: a JSON syntax error with its line number, a missing field, or a car check error |
| `stopped at t = ...: the car left the ground` | the car drove or flew off the track. Make the track wider, or stop earlier |
| The car won't climb onto something | the edge is taller than about 2 cm. Add a `ramp` piece in front of it |
| The car flips when you brake in the air | that's real physics: braking spinning wheels in the air pitches the car nose-down. Brake after landing |
| Compile errors after editing C# | Unity's **Console** shows the file and line. Fix them, or restore your copy |

---

## 12. Folder map

```
Simulation\
  WALKTHROUGH.md            this file
  run-unity-experiment.ps1  run experiments headless
  cars\                     car files: udirc_1610s.json (reference), your cars, mystery *.car
  config\scene.json         scripted driver schedule, controller gains, sensor settings, ground grip
  experiments\              experiment files + README.md (the full reference)
    environments\           track files
  output\unity_experiments\ results, one folder per run
  teacher\                  mystery-car answers + tool (don't hand out)
  Unity\AvtokrobatViz\      the Unity project (open this in Unity Hub)
    Assets\Scripts\         the simulator's C# source
  src\, docker\             the older Chrono engine (not needed)
```
