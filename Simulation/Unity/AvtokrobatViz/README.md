# Avtokrobat — Unity simulator

A simulator of the **UDIRC 1610S** (1/16 4WD brushless monster truck) in Unity. It covers the
car's physics, its sensors, a VESC motor controller and the on-board controller, on
environments you build from JSON. No Docker, no Chrono.

```
driver (scripted / keyboard / experiment file)
  -> UdircController   on-board controller (C# mirror of src/firmware/ak_controller.c)
  -> steering servo    600 deg/s slew limit
  -> VescModel         torque (current) control of a 2845 4700KV motor on 3S
  -> gearbox 11.4:1    locked centre shaft, open front + rear diffs
  -> wheels + tyres    raycast suspension, wheel spin, Pacejka-style tyre forces
  -> PhysX Rigidbody   the chassis (1.40 kg) + a body collider for crashes
  -> UnitySensors      accelerometer, Hall motor sensor, 8x8 lidar
```

Physics and control share one **1 kHz** clock (dt = 1 ms), stepped by hand, so runs are
repeatable.

## Requirements

- **Unity 6000.5.2f1** (the project was compiled with it). Open `Unity/AvtokrobatViz` in Unity Hub.
- Nothing else. The car and the driver schedule are read from `../../config/`, and the
  environments and experiments from `../../experiments/`.

## Run it

Press **Play** on any scene. `AvtokrobatBootstrap` starts the simulator. **Avtokrobat > Create
Play Scene** saves a scene with it preset, which you need for a standalone build.

The panel on the left:

- **Car:** pick one from `cars/`: the reference `udirc_1610s`, students' own `*.json` cars, or
  encrypted mystery cars `*.car`. Car-file warnings show here, and **Spec sheet** lists what
  the numbers imply. A mystery car hides its parameter sliders, true-state telemetry and jump
  summary; you see only sensors and the VESC. See `experiments/README.md` §10–11.
- **Environment:** pick one (`experiments/environments/*.json` plus the built-in `jump` and
  `flat`). **Reset** reloads the files, so you can edit a JSON and see the result straight away.
- **Simulation:** Run / Pause, Step 10 ms, Reset, rate 0.1x–2x.
- **Driver:**
  - *Scripted* follows the `config/scene.json` schedule. The throttle phase is a speed target
    (`launch_speed_m_s`), then it coasts and brakes after landing.
  - *Keyboard*: W gives full throttle. S brakes (45% of max torque) while the car rolls
    forward, then reverses once it has stopped. A/D steer.
- **Parameters:** launch speed, mass, grip, spring, damper, travel, COM height, VESC delay and
  current-loop τ, battery cell voltage. They're applied on Reset. **From files** restores them.
- **Experiments:** runs any `experiments/*.json`, either watched or fast. **Open results in
  folder** opens the run's CSVs.
- **Telemetry:** pose, speed, accelerometer and lidar frame, and the controller and VESC state
  (requested vs seen torque, current, duty, bus voltage, battery current, motor rpm). It also
  shows the first jump's launch, apex, air time and landing.

Camera: drag outside the panel to orbit, wheel to zoom, 1-4 for views. Space runs or pauses,
R resets and runs.

## Experiments without Play mode

```powershell
.\run-unity-experiment.ps1 experiments            # from the Simulation folder; the editor may stay open
```

In the editor you can also use **Avtokrobat > Run Experiment File...** or **Run All
Experiments**. Each run writes `output/unity_experiments/<name>_<yyyyMMdd-HHmmss>/`, which holds
`internal.csv`, `external.csv`, `truth.csv` and `experiment.json`. Formats and the environment
reference are in `experiments/README.md` (sections 4–9).

## The car model (`UdircSim.cs`)

- **Numbers:** they come from `cars/udirc_1610s.json`: geometry (wheelbase/track 180 mm, tyre
  85 × 44.5 mm), mass, inertia, motor, battery, VESC and `unity_model` (suspension, steering,
  tyre, brake fractions, sensor mounts). Online sources and estimates are noted in the file.
- **VESC** (`VescModel.cs`): the command arrives after a transport latency
  (`command_delay_s`, 2 ms) and drives a first-order FOC current loop
  (`current_time_constant_s`, 1 ms). Current is limited by the motor limits, by the voltage left
  over after back-EMF ((±0.95 V_bus − ω/KV)/R) and by the battery limits. The battery sags
  under load. Shaft torque = Kt·I − drag.
- **Wheels:** computed in the open, not by Unity's `WheelCollider`. That component is tuned
  for full-size cars: it added hidden spin inertia, lost most of the drive force and held the
  small car still at low speed. Per wheel the model has:
  - a raycast spring-damper suspension with a bump stop;
  - slip-ratio and slip-angle tyre forces on a friction circle, with per-surface μ;
  - the 1610S drivetrain: a locked centre shaft and open diffs with a little internal friction.
- **Spin reaction:** every step the chassis gets −dL/dt of the wheels (about the axles) and of
  the motor rotor (about `motor_axis`), so wheel torque rotates the car in the air. Braking the
  wheels in mid-air pitches it nose-down hard.
- **Performance (flat, full throttle):** 0→10 m/s in 1.35 s, 0→20 m/s in about 2.9 s, top speed
  about 20.4 m/s. The motor's no-load limit at 12 V sets the top speed; the run-up is
  traction-limited at about 0.85 g with wheelspin.
- **Still estimates (measure the real car):** mass, inertia, steering angle, suspension rate,
  tyre parameters, motor R / no-load current / rotor inertia.

## Coordinates

Config, environments, experiments and CSVs use the **experiment frame**: right-handed, z up,
x forward (towards the ramp), y left. Unity is left-handed, y up. `FrameConvert.cs` is the only
place that converts: `Unity(x, y, z) = experiment(x, z, y)`.

## Files

| File | Role |
|---|---|
| `UnityPhysicsController.cs` | the panel: environment, driving, parameters, experiments, telemetry, camera |
| `UdircSim.cs` | the car: suspension, drivetrain, tyres, spin reaction, stepping, initial state |
| `VescModel.cs`, `UdircController.cs`, `UnitySensors.cs` | motor controller, on-board controller, sensors |
| `UdircSpec.cs`, `SimConfig.cs` | the car-file format, and the scene config |
| `CarLibrary.cs` | car files: find, load, decrypt mystery cars, check (errors and warnings), spec sheet |
| `EnvironmentBuilder.cs` | `ground` / `ramp` / `box` pieces from JSON, per-surface μ |
| `ExperimentRunner.cs`, `Signals.cs`, `MiniJson.cs` | experiment files, input signals, JSON |
| `SceneBuilder.cs` | the visual car (UDIRC look-alike from primitives) |
| `Editor/ExperimentCli.cs`, `Editor/AvtokrobatMenu.cs` | headless runner, editor menu items |

The Chrono viewer and the live-server client were removed on 2026-10-25. A copy is in
`../_removed_chrono_backup/`. The Python/Chrono side of the project (`src/`, `docker/`) is
unchanged.
