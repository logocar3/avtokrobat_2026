# Avtokrobat — Unity simulator

A simulator of the UDIRC 1610S RC car (1/16 4WD brushless monster truck) in Unity: car physics,
a VESC motor controller, the on-board controller at 1 kHz, sensors (accelerometer, Hall motor
sensor, 8x8 lidar), and tracks, cars and experiments you build from JSON files.

**Start here: [WALKTHROUGH.md](WALKTHROUGH.md)**. It covers installing Unity 6000.5.2f1,
opening `Unity/AvtokrobatViz`, running the simulator and experiments, and building your own.

| Folder | |
|---|---|
| `Unity/AvtokrobatViz/` | the Unity project (open it in Unity Hub) |
| `cars/` | car files: `udirc_1610s.json` (the real car), an example student car, mystery cars (`*.car`) |
| `experiments/` | experiment files and the full reference (`experiments/README.md`); `environments/` holds the tracks |
| `config/scene.json` | scripted driver schedule, controller gains, sensor settings, ground grip |
| `run-unity-experiment.ps1` | runs experiments headless: `.\run-unity-experiment.ps1 experiments` |

Keep the folders side by side as they are: the simulator finds `cars/`, `config/` and
`experiments/` relative to the Unity project.
